using DpmAgent.Api.Agenda;

namespace DpmAgent.Api.Demo;

public static class DemoEndpoints
{
    public const string PoliticaReinicio = "demo-reinicio";

    public static IEndpointRouteBuilder MapDemo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/demo", (ServicioDemo demo) => Results.Ok(demo.Info()));

        app.MapPost("/api/demo/reiniciar", async (ServicioDemo demo, Bitacora bitacora, HttpContext http, CancellationToken ct) =>
        {
            try
            {
                await demo.ReiniciarAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }

            await bitacora.RegistrarAsync("demo", "demo_reiniciada", "consultorio", null, null, null, ct);
            return Results.Ok(new { reiniciada = true });
        }).RequireRateLimiting(PoliticaReinicio);

        return app;
    }

    /// <summary>Al arrancar con la demo habilitada, deja el escenario listo.</summary>
    public static async Task PrepararDemoAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var demo = scope.ServiceProvider.GetRequiredService<ServicioDemo>();
        if (demo.Info().Habilitado)
            await demo.PrepararEscenarioAsync(CancellationToken.None);
    }
}
