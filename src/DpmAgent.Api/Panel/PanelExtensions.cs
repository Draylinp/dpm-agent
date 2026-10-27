using System.Security.Claims;
using DpmAgent.Api.Data;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DpmAgent.Api.Panel;

public static class PanelExtensions
{
    public const string Politica = "Panel";
    public const string PoliticaAdmin = "PanelAdmin";

    /// <summary>Sesión iniciada aunque falte cambiar la clave o activar el segundo factor (solo para hacerlo).</summary>
    public const string PoliticaSesion = "PanelSesion";

    public static IServiceCollection AddPanel(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.Configure<PanelOptions>(config.GetSection(PanelOptions.SectionName));
        var opciones = config.GetSection(PanelOptions.SectionName).Get<PanelOptions>() ?? new PanelOptions();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "dpm.panel";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = env.IsDevelopment() || env.IsEnvironment("Testing")
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                // Inactividad: la cookie vence a los N minutos y cada petición la renueva.
                o.ExpireTimeSpan = TimeSpan.FromMinutes(opciones.MinutosInactividad);
                o.SlidingExpiration = true;
                // Expiración absoluta y sesiones invalidadas: al cambiar la clave o el segundo factor cambia el sello.
                o.Events.OnValidatePrincipal = async c =>
                {
                    var reloj = c.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                    // Ámbito propio: la base de esta consulta no debe fijar el consultorio de la petición (eso viene después).
                    await using var ambito = c.HttpContext.RequestServices.CreateAsyncScope();
                    var vigente = long.TryParse(c.Principal?.FindFirstValue(ServicioUsuarios.ClaimInicio), NumberStyles.None, CultureInfo.InvariantCulture, out var inicio)
                                  && reloj.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(inicio) <= TimeSpan.FromHours(opciones.HorasMaximasSesion)
                                  && await ambito.ServiceProvider.GetRequiredService<ServicioUsuarios>().DeSesionAsync(c.Principal!, c.HttpContext.RequestAborted) is not null;
                    if (!vigente)
                    {
                        c.RejectPrincipal();
                        await c.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
                // Es una API: sin redirecciones a páginas de login.
                o.Events.OnRedirectToLogin = c =>
                {
                    c.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = c =>
                {
                    c.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaSesion, p => p.RequireAuthenticatedUser().RequireClaim(ServicioUsuarios.ClaimConsultorio))
            // Con la clave inicial sin cambiar o sin el segundo factor exigido, no se ven datos del consultorio.
            .AddPolicy(Politica, p => p.RequireAuthenticatedUser().RequireClaim(ServicioUsuarios.ClaimConsultorio)
                .RequireAssertion(c => !c.User.HasClaim(x => x.Type == ServicioUsuarios.ClaimRestriccion)))
            .AddPolicy(PoliticaAdmin, p => p.RequireAuthenticatedUser().RequireClaim(ServicioUsuarios.ClaimConsultorio).RequireRole("admin")
                .RequireAssertion(c => !c.User.HasClaim(x => x.Type == ServicioUsuarios.ClaimRestriccion)));

        services.AddScoped<ServicioUsuarios>();
        services.AddScoped<ServicioPanel>();
        return services;
    }

    /// <summary>
    /// Autenticación y, para quien entró al panel, fija el consultorio de la petición antes
    /// de que se use la base de datos: una secretaria solo ve los datos de su consultorio.
    /// </summary>
    public static IApplicationBuilder UsePanel(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.Use(async (contexto, siguiente) =>
        {
            if (contexto.User.FindFirstValue(ServicioUsuarios.ClaimConsultorio) is { } valor && int.TryParse(valor, out var consultorio))
                contexto.RequestServices.GetRequiredService<ConsultorioDelAmbito>().Establecer(consultorio);
            await siguiente();
        });
        app.UseAuthorization();
        return app;
    }

    public static async Task AsegurarUsuarioInicialAsync(this IServiceProvider services, bool esDesarrollo)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Panel");
        await scope.ServiceProvider.GetRequiredService<ServicioUsuarios>().AsegurarUsuarioInicialAsync(esDesarrollo, logger, CancellationToken.None);
    }

    /// <summary>Actor para la bitácora: "secretaria:usuario".</summary>
    public static string Actor(this ClaimsPrincipal usuario) => $"secretaria:{usuario.Identity?.Name ?? "?"}";
}
