using System.Security.Claims;
using DpmAgent.Api.Data;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DpmAgent.Api.Panel;

public static class PanelExtensions
{
    public const string Politica = "Panel";
    public const string PoliticaAdmin = "PanelAdmin";

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
                o.ExpireTimeSpan = TimeSpan.FromMinutes(opciones.MinutosSesion);
                o.SlidingExpiration = true;
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
            .AddPolicy(Politica, p => p.RequireAuthenticatedUser().RequireClaim(ServicioUsuarios.ClaimConsultorio))
            .AddPolicy(PoliticaAdmin, p => p.RequireAuthenticatedUser().RequireClaim(ServicioUsuarios.ClaimConsultorio).RequireRole("admin"));

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
