using System.Security.Claims;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

public sealed record RetrasoRequest(int Minutos);

/// <param name="Confirmado">La secretaria confirmó en persona que el nuevo paciente se gestiona desde ese número.</param>
public sealed record FamiliarRequest(string Telefono, string Nombre, bool Confirmado);

/// <summary>Panel: reprogramaciones por bloqueos, retraso del médico, ausencias y familias (varios pacientes con un teléfono).</summary>
public static class CambiosAgendaEndpoints
{
    public static IEndpointRouteBuilder MapCambiosAgenda(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel").RequireAuthorization(PanelExtensions.Politica);

        panel.MapGet("/reprogramaciones", async (ServicioReprogramacion s, CancellationToken ct) => Results.Ok(await s.ListarAsync(ct)));

        panel.MapPost("/medicos/{id}/retraso", async (string id, RetrasoRequest req, ClaimsPrincipal user, ServicioReprogramacion s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(new { avisados = await s.AvisarRetrasoAsync(id, req.Minutos, user.Actor(), ct) });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        panel.MapPost("/citas/{id}/no-asistio", async (string id, ClaimsPrincipal user, ServicioReprogramacion s, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(new { ausencias = await s.MarcarAusenciaAsync(id, user.Actor(), ct) });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // Incluye a los desactivados y a los no localizables, para que la secretaria los vea y los reactive.
        panel.MapGet("/familias", async (string? telefono, ClaimsPrincipal user, DpmDbContext db, ServicioConsentimiento consentimientos, TimeProvider clock, CancellationToken ct) =>
        {
            if (Telefono.Normalizar(telefono) is not { } normalizado)
                return Results.BadRequest(new { error = "Indique un teléfono válido." });
            var familia = await db.Pacientes.AsNoTracking().Where(p => p.Telefono == normalizado).OrderBy(p => p.Id).ToListAsync(ct);
            var ausencias = await ServicioReprogramacion.AusenciasAsync(db, familia.Select(p => p.Id), ct);
            await RegistroAccesos.RegistrarAsync(db, user, RegistroAccesos.VerFicha, familia.Select(p => p.Id), clock, ct);
            var lista = new List<object>();
            foreach (var p in familia)
            {
                lista.Add(new
                {
                    id = IdsDpm.Paciente(p.Id), nombre = p.Nombre, activo = p.Activo, noLocalizable = p.NoLocalizableUtc is not null,
                    ausencias = ausencias.GetValueOrDefault(p.Id), consentimientos = await consentimientos.EstadoAsync(p.Id, ct),
                });
            }

            return Results.Ok(lista);
        });

        panel.MapPost("/pacientes/{id}/desactivar", async (string id, ClaimsPrincipal user, ServicioContacto s, CancellationToken ct) =>
            IdsDpm.Leer(id, "PAC") is { } pid && await s.DesactivarAsync(pid, user.Actor(), ct) ? Results.Ok(new { activo = false }) : Results.NotFound());

        panel.MapPost("/pacientes/{id}/reactivar", async (string id, ClaimsPrincipal user, ServicioContacto s, CancellationToken ct) =>
            IdsDpm.Leer(id, "PAC") is { } pid && await s.ReactivarAsync(pid, user.Actor(), ct) ? Results.Ok(new { activo = true }) : Results.NotFound());

        // Vincular a un familiar a un número que ya tiene pacientes: la secretaria debe confirmarlo (lo dijo el titular en persona).
        panel.MapPost("/familias", async (FamiliarRequest req, ClaimsPrincipal user, IDpmGateway dpm, Bitacora bitacora, CancellationToken ct) =>
        {
            if (Telefono.Normalizar(req.Telefono) is null || string.IsNullOrWhiteSpace(req.Nombre) || req.Nombre.Trim().Length > 200)
                return Results.BadRequest(new { error = "Indique un teléfono válido y el nombre completo." });
            var familia = await dpm.PacientesDelTelefonoAsync(req.Telefono, ct);
            if (familia.Count > 0 && !req.Confirmado)
                return Results.BadRequest(new { error = "Ese número ya gestiona a otros pacientes: confirme que el titular autorizó vincular a este paciente." });
            var p = await dpm.RegistrarPacienteAsync(req.Nombre, req.Telefono, null, ct);
            await bitacora.RegistrarAsync(user.Actor(), familia.Count > 0 ? "paciente_vinculado" : "paciente_registrado", "paciente", p.Id, null, null, ct);
            return Results.Ok(new { id = p.Id, nombre = p.Nombre, vinculado = familia.Count > 0 });
        });

        return app;
    }
}
