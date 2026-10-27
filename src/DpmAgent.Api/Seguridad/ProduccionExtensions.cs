using System.Text.Json;
using System.Threading.RateLimiting;
using DpmAgent.Api.Data;
using DpmAgent.Api.Demo;
using DpmAgent.Api.Model;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Seguridad;

/// <summary>Rate limiting, salud, logs, cabeceras y demás piezas para correr en producción.</summary>
public static class ProduccionExtensions
{
    public const string PoliticaPublica = "publico";
    public const string PoliticaLogin = "login";
    public const string EncabezadoCorrelacion = "X-Correlation-Id";

    public static WebApplicationBuilder AddProduccion(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;

        services.Configure<LimitesOptions>(config.GetSection(LimitesOptions.SectionName));
        var limites = config.GetSection(LimitesOptions.SectionName).Get<LimitesOptions>() ?? new LimitesOptions();
        services.AddSingleton<LimitadorSesiones>();

        // Logs: JSON con scopes (SesionId, CorrelationId) para el agregador; texto legible en desarrollo.
        if (string.Equals(config["Logging:Formato"], "json", StringComparison.OrdinalIgnoreCase))
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(o =>
            {
                o.IncludeScopes = true;
                o.UseUtcTimestamp = true;
                o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            });
        }
        else
        {
            builder.Logging.AddSimpleConsole(o => o.IncludeScopes = true);
        }

        // Errores no controlados: ASP.NET registra la excepción completa (puede traer datos del paciente);
        // se apaga ese log y la registra ManejadorErroresPrivado ya enmascarada.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
        services.AddExceptionHandler<ManejadorErroresPrivado>();

        if (limites.ConfiarEncabezadosReenviados)
        {
            services.Configure<ForwardedHeadersOptions>(o =>
            {
                o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                // El proxy de la plataforma (App Service, balanceador) no tiene IP fija conocida.
                o.KnownNetworks.Clear();
                o.KnownProxies.Clear();
                o.ForwardLimit = 1;
            });
        }

        var demo = config.GetSection(DemoOptions.SectionName).Get<DemoOptions>() ?? new DemoOptions();

        services.AddRateLimiter(o =>
        {
            // Demo pública: además del límite por minuto, un tope diario por IP en todo lo que llama al modelo.
            if (demo.Habilitado)
            {
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http => LlamaAlModelo(http.Request)
                    ? RateLimitPartition.GetFixedWindowLimiter("demo-dia:" + Ip(http), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = Math.Max(1, demo.MensajesPorDiaPorIp),
                        Window = TimeSpan.FromDays(1),
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter(""));
            }

            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (contexto, ct) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    contexto.HttpContext.Response.Headers.RetryAfter = ((int)espera.TotalSeconds).ToString();
                // En el idioma de la interfaz (Accept-Language). Una espera de horas es el tope diario de la demo.
                var idioma = Agent.RespuestasAgente.IdiomaDe(contexto.HttpContext.Request);
                var clave = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var resto) && resto > TimeSpan.FromHours(1)
                    ? "entrada.limite_demo"
                    : "entrada.demasiadas";
                await contexto.HttpContext.Response.WriteAsJsonAsync(new { error = Idiomas.Textos.Obtener(clave, idioma) }, ct);
            };

            o.AddPolicy(PoliticaPublica, http => RateLimitPartition.GetFixedWindowLimiter(Ip(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limites.PeticionesPorMinutoPorIp),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

            o.AddPolicy(DemoEndpoints.PoliticaReinicio, http => RateLimitPartition.GetFixedWindowLimiter(Ip(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, demo.ReiniciosPorHoraPorIp),
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0,
            }));

            o.AddPolicy(PoliticaLogin, http => RateLimitPartition.GetFixedWindowLimiter(Ip(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limites.LoginPorMinutoPorIp),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
        });

        services.AddHealthChecks()
            .AddCheck<SaludBaseDatos>("base_datos", tags: ["ready"])
            .AddCheck<SaludModelo>("modelo", tags: ["ready"]);

        // Las cookies del panel se cifran con estas claves: deben sobrevivir reinicios y ser las mismas
        // en todas las instancias. App Service las guarda solo; en contenedores, monte un volumen.
        if (config["DataProtection:Ruta"] is { Length: > 0 } ruta)
            services.AddDataProtection().SetApplicationName("dpm-agent").PersistKeysToFileSystem(new DirectoryInfo(ruta));

        services.Configure<RetencionOptions>(config.GetSection(RetencionOptions.SectionName));
        services.AddScoped<ServicioRetencion>();
        services.AddHostedService<RetencionHostedService>();

        return builder;
    }

    public static WebApplication UseProduccion(this WebApplication app)
    {
        if (app.Services.GetRequiredService<IOptions<LimitesOptions>>().Value.ConfiarEncabezadosReenviados)
            app.UseForwardedHeaders();

        // Correlación: cada petición lleva un id que aparece en todos sus logs y vuelve en la respuesta.
        app.Use(async (contexto, siguiente) =>
        {
            var id = contexto.Request.Headers[EncabezadoCorrelacion].ToString();
            if (id.Length is 0 or > 64 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                id = contexto.TraceIdentifier;

            contexto.Response.Headers[EncabezadoCorrelacion] = id;
            var logger = contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Peticion");
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
                await siguiente();
        });

        app.Use(async (contexto, siguiente) =>
        {
            var h = contexto.Response.Headers;
            h.XContentTypeOptions = "nosniff";
            h.XFrameOptions = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(self)";
            h.ContentSecurityPolicy =
                "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
                "img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            await siguiente();
        });

        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        app.UseRateLimiter();
        return app;
    }

    public static WebApplication MapSalud(this WebApplication app)
    {
        // Vida: el proceso responde. Listo: además hay base de datos y modelo configurado.
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = EscribirSalud });
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = EscribirSalud });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready"), ResponseWriter = EscribirSalud });
        return app;
    }

    private static Task EscribirSalud(HttpContext contexto, HealthReport reporte)
    {
        contexto.Response.ContentType = "application/json";
        return contexto.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            estado = reporte.Status.ToString(),
            hora = DateTimeOffset.UtcNow,
            componentes = reporte.Entries.ToDictionary(e => e.Key, e => new { estado = e.Value.Status.ToString(), detalle = e.Value.Description }),
        }));
    }

    /// <summary>Endpoints públicos que gastan llamadas al modelo (texto, voz y foto del carnet).</summary>
    internal static bool LlamaAlModelo(HttpRequest r) =>
        HttpMethods.IsPost(r.Method) && (r.Path.Equals("/api/chat") || r.Path.Equals("/api/chat/audio") || r.Path.Equals("/api/pacientes/carnet"));

    private static string Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
}

/// <summary>La base de datos responde.</summary>
public sealed class SaludBaseDatos(DpmDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("No hay conexión con la base de datos.");
}

/// <summary>El modelo principal tiene lo necesario para funcionar (sin llamarlo: no gasta tokens).</summary>
public sealed class SaludModelo(IConfiguration config) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var proveedor = (config["Modelo:Proveedor"] ?? "MetaApi").Trim().ToLowerInvariant();
        var faltante = proveedor == ModelosExtensions.OpenAICompatible
            ? string.IsNullOrWhiteSpace(config["OpenAICompatible:Model"]) ? "Falta OpenAICompatible:Model." : null
            : string.IsNullOrWhiteSpace(config["Meta:ApiKey"] ?? Environment.GetEnvironmentVariable("META_API_KEY")) ? "Falta la clave de la Meta Model API." : null;

        return Task.FromResult(faltante is null ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(faltante));
    }
}
