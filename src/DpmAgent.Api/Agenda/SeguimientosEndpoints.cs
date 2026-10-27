using System.Globalization;
using System.Security.Claims;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

/// <param name="FechaObjetivo">yyyy-MM-dd. Si no se da, se calcula con EnCantidad + EnUnidad (semanas o meses desde hoy).</param>
public sealed record SeguimientoRequest(
    string PacienteId, string MedicoId, string? FechaObjetivo, int? EnCantidad, string? EnUnidad, int ToleranciaDias,
    string? Motivo, int? RecurrenciaCada, string? RecurrenciaUnidad);

public sealed record FeriadoRequest(string Fecha, string Nombre);

public sealed record BloqueoRequest(string MedicoId, string Desde, string Hasta, string? Motivo);

public sealed record ResultadoBloqueo(int Id, int CitasParaReprogramar, int SeguimientosEnElRango);

public static class SeguimientosEndpoints
{
    public static readonly string[] MotivosBloqueo = ["vacaciones", "congreso", "licencia", "otro"];

    public static IEndpointRouteBuilder MapSeguimientos(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel").RequireAuthorization(PanelExtensions.Politica);

        panel.MapGet("/seguimientos", async (string? estado, ServicioSeguimientos s, CancellationToken ct) =>
            Results.Ok(await s.ListarAsync(estado, ct)));

        panel.MapPost("/seguimientos", async (
            SeguimientoRequest req, ClaimsPrincipal user, ServicioSeguimientos s, ServicioConsentimiento consentimientos, TimeProvider clock, CancellationToken ct) =>
        {
            try
            {
                var fecha = FechaObjetivo(req, HorarioConsultorio.FechaRd(clock.GetUtcNow()));
                var creado = await s.CrearAsync(new NuevoSeguimiento(req.PacienteId, req.MedicoId, fecha, req.ToleranciaDias, req.Motivo,
                    req.RecurrenciaCada, req.RecurrenciaUnidad), user.Actor(), ct);

                // Si el paciente no dio permiso para avisos de seguimiento, el panel ofrece registrarlo en persona.
                var pid = IdsDpm.Leer(creado.PacienteId, "PAC")!.Value;
                var conPermiso = await consentimientos.VigenteAsync(pid, CategoriasConsentimiento.Seguimientos, ct);
                return Results.Ok(new { seguimiento = creado, requiereConsentimiento = !conPermiso });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        panel.MapPost("/seguimientos/{id}/cancelar", async (string id, ClaimsPrincipal user, ServicioSeguimientos s, CancellationToken ct) =>
            await s.CancelarAsync(id, user.Actor(), ct) ? Results.Ok(new { cancelado = true }) : Results.NotFound());

        panel.MapPost("/seguimientos/{id}/reabrir", async (string id, ClaimsPrincipal user, ServicioSeguimientos s, CancellationToken ct) =>
            await s.ReabrirAsync(id, user.Actor(), ct) ? Results.Ok(new { reabierto = true }) : Results.NotFound());

        panel.MapGet("/feriados", async (DpmDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Feriados.AsNoTracking().OrderBy(f => f.Fecha).Select(f => new { f.Id, fecha = f.Fecha, f.Nombre }).ToListAsync(ct)));

        panel.MapPost("/feriados", async (FeriadoRequest req, ClaimsPrincipal user, DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            if (!DateOnly.TryParseExact(req.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ||
                string.IsNullOrWhiteSpace(req.Nombre) || req.Nombre.Length > 100)
                return Results.BadRequest(new { error = "Indique la fecha (yyyy-MM-dd) y un nombre de hasta 100 caracteres." });
            if (await db.Feriados.AnyAsync(f => f.Fecha == fecha, ct))
                return Results.Conflict(new { error = "Ese día ya es feriado." });
            db.Feriados.Add(new Feriado { ConsultorioId = db.ConsultorioId, Fecha = fecha, Nombre = req.Nombre.Trim() });
            await db.SaveChangesAsync(ct);
            await bitacora.RegistrarAsync(user.Actor(), "feriado_agregado", "consultorio", null, null, fecha.ToString("yyyy-MM-dd"), ct);
            return Results.Ok(new { agregado = true });
        }).RequireAuthorization(PanelExtensions.PoliticaAdmin);

        panel.MapDelete("/feriados/{id:int}", async (int id, ClaimsPrincipal user, DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            var n = await db.Feriados.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
            if (n == 0)
                return Results.NotFound();
            await bitacora.RegistrarAsync(user.Actor(), "feriado_quitado", "consultorio", null, null, null, ct);
            return Results.Ok(new { quitado = true });
        }).RequireAuthorization(PanelExtensions.PoliticaAdmin);

        panel.MapGet("/bloqueos", async (DpmDbContext db, CancellationToken ct) =>
            Results.Ok(await db.BloqueosAgenda.AsNoTracking().OrderBy(b => b.Desde)
                .Select(b => new { b.Id, medicoId = IdsDpm.Medico(b.MedicoId), b.Desde, b.Hasta, b.Motivo }).ToListAsync(ct)));

        panel.MapPost("/bloqueos", async (BloqueoRequest req, ClaimsPrincipal user, ServicioAgendaMedico agenda, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await agenda.BloquearAsync(req.MedicoId, req.Desde, req.Hasta, req.Motivo, user.Actor(), ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        return app;
    }

    internal static DateOnly FechaObjetivo(SeguimientoRequest req, DateOnly hoy)
    {
        if (!string.IsNullOrWhiteSpace(req.FechaObjetivo))
            return DateOnly.ParseExact(req.FechaObjetivo, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (req.EnCantidad, req.EnUnidad?.Trim().ToLowerInvariant()) switch
        {
            ({ } n and > 0, "semanas") => hoy.AddDays(7 * n),
            ({ } n and > 0, "meses") => hoy.AddMonths(n),
            _ => throw new ArgumentException("Indique la fecha objetivo o en cuántas semanas o meses."),
        };
    }
}

/// <summary>Bloqueos de agenda del médico (vacaciones, congresos). Lo usan el panel y el canal del médico.</summary>
public sealed class ServicioAgendaMedico(DpmDbContext db, Bitacora bitacora, TimeProvider clock)
{
    public async Task<ResultadoBloqueo> BloquearAsync(string medicoId, string desde, string hasta, string? motivo, string actor, CancellationToken ct)
    {
        var medico = IdsDpm.Leer(medicoId, "MED") is { } m && await db.Medicos.AnyAsync(x => x.Id == m, ct)
            ? m : throw new ArgumentException($"No existe el médico {medicoId}.");
        if (!DateOnly.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ||
            !DateOnly.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var h) || h < d)
            throw new ArgumentException("Indique desde y hasta (yyyy-MM-dd), con hasta igual o posterior a desde.");
        if (h.DayNumber - d.DayNumber > 90)
            throw new ArgumentException("Un bloqueo no puede pasar de 90 días.");
        var motivoLimpio = motivo?.Trim().ToLowerInvariant() is { } x && SeguimientosEndpoints.MotivosBloqueo.Contains(x) ? x : "otro";

        var bloqueo = new BloqueoAgenda
        {
            ConsultorioId = db.ConsultorioId, MedicoId = medico, Desde = d, Hasta = h, Motivo = motivoLimpio,
            CreadoPor = actor.Length > 60 ? actor[..60] : actor, CreadoUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.BloqueosAgenda.Add(bloqueo);

        // Citas que ya estaban en esos días: quedan marcadas para reprogramar (la secretaria o el agente las mueven).
        var inicio = HorarioConsultorio.EnHoraRd(d, TimeOnly.MinValue).UtcDateTime;
        var fin = HorarioConsultorio.EnHoraRd(h.AddDays(1), TimeOnly.MinValue).UtcDateTime;
        var afectadas = await db.Citas.Where(c => c.MedicoId == medico && c.Estado == "confirmada" && c.FechaHoraUtc >= inicio && c.FechaHoraUtc < fin).ToListAsync(ct);
        foreach (var c in afectadas)
        {
            c.ParaReprogramar = true;
            c.Reprogramacion = ServicioReprogramacion.Pendiente; // el motor contacta a cada paciente con opciones
        }
        var seguimientos = await db.Seguimientos.CountAsync(s => s.MedicoId == medico && s.Estado != "cancelado" && s.Estado != "agendado"
                                                                  && s.FechaObjetivo >= d && s.FechaObjetivo <= h, ct);
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, "agenda_bloqueada", "medico", IdsDpm.Medico(medico), null,
            $"{d:yyyy-MM-dd} a {h:yyyy-MM-dd} ({motivoLimpio}); {afectadas.Count} citas para reprogramar", ct);
        return new ResultadoBloqueo(bloqueo.Id, afectadas.Count, seguimientos);
    }
}
