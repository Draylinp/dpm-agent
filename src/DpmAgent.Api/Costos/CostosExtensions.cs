using DpmAgent.Api.Model;
using DpmAgent.Api.Panel;

namespace DpmAgent.Api.Costos;

public static class CostosExtensions
{
    public static IServiceCollection AddCostos(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<PreciosOptions>(config.GetSection(PreciosOptions.SectionName));
        services.AddSingleton<CalculadoraCosto>();
        services.AddSingleton<ColaUso>();
        services.AddSingleton<IRegistroUso>(sp => sp.GetRequiredService<ColaUso>());
        services.AddHostedService<GuardadoUsoHostedService>();
        services.AddScoped<ServicioCostos>();
        return services;
    }

    public static IEndpointRouteBuilder MapCostos(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/panel/costos", async (int? dias, ServicioCostos costos, CancellationToken ct) =>
            Results.Ok(await costos.ResumenAsync(dias ?? 30, ct))).RequireAuthorization(PanelExtensions.Politica);
        return app;
    }
}
