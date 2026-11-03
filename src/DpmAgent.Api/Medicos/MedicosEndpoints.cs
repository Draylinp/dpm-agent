using System.Security.Claims;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;

namespace DpmAgent.Api.Medicos;

public sealed record TelefonoMedicoRequest(string Telefono);

public sealed record ResumenMedicoRequest(bool Acepta);

public static class MedicosEndpoints
{
    public static IServiceCollection AddCanalMedico(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CanalMedicoOptions>(config.GetSection(CanalMedicoOptions.SectionName));
        services.AddScoped<ServicioMedicos>();
        services.AddScoped<Agent.HerramientasMedico>();
        return services;
    }

    public static IEndpointRouteBuilder MapMedicos(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel/medicos").RequireAuthorization(PanelExtensions.Politica);

        panel.MapGet("", async (ServicioMedicos medicos, CancellationToken ct) => Results.Ok(await medicos.ListarAsync(ct)));

        // Solo un admin registra el teléfono; el código se muestra una vez y el médico lo envía desde ese teléfono.
        panel.MapPost("/{id}/telefono", async (string id, TelefonoMedicoRequest req, ClaimsPrincipal user, ServicioMedicos medicos, CancellationToken ct) =>
        {
            try
            {
                var codigo = await medicos.RegistrarTelefonoAsync(id, req.Telefono, user.Actor(), ct);
                return Results.Ok(new { codigo, instruccion = $"Pida al médico que envíe \"VERIFICAR {codigo}\" desde ese número por WhatsApp." });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).RequireAuthorization(PanelExtensions.PoliticaAdmin);

        // Permiso del médico para el resumen diario, registrado en persona.
        panel.MapPost("/{id}/resumen", async (string id, ResumenMedicoRequest req, ClaimsPrincipal user, ServicioMedicos medicos, CancellationToken ct) =>
        {
            if (IdsDpm.Leer(id, "MED") is not { } mid)
                return Results.NotFound();
            await medicos.RegistrarResumenAsync(mid, req.Acepta, CanalesConsentimiento.Presencial, user.Actor(), ct);
            return Results.Ok(new { resumen = req.Acepta });
        });

        return app;
    }
}
