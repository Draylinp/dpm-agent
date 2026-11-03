using System.Globalization;
using System.Security.Claims;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

/// <param name="Hora">HH:mm, obligatoria para aprobar.</param>
public sealed record DecisionSobrecupoRequest(bool Aprobar, string? Hora);

/// <param name="DiaSemana">1 = lunes … 5 = viernes.</param>
public sealed record CupoUrgenciaRequest(string MedicoId, int DiaSemana, string Hora);

/// <param name="CentrosEmergencia">Uno por línea; se muestran fuera de horario.</param>
public sealed record ConfigUrgenciasRequest(int? HoraLiberarCupos, string? CentrosEmergencia);

public static class UrgenciasEndpoints
{
    public static IEndpointRouteBuilder MapUrgencias(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel/urgencias").RequireAuthorization(PanelExtensions.Politica);

        panel.MapGet("/sobrecupos", async (string? estado, ServicioUrgencias s, CancellationToken ct) =>
            Results.Ok(await s.ListarAsync(estado, null, ct)));

        // Solo el personal decide: el agente nunca aprueba un sobrecupo.
        panel.MapPost("/sobrecupos/{id}/decidir", async (string id, DecisionSobrecupoRequest req, ClaimsPrincipal user, ServicioUrgencias s, CancellationToken ct) =>
        {
            try
            {
                TimeOnly? hora = req.Hora is { Length: > 0 } h ? TimeOnly.ParseExact(h, "HH:mm", CultureInfo.InvariantCulture) : null;
                return Results.Ok(await s.DecidirAsync(id, req.Aprobar, hora, user.Actor(), null, ct));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        panel.MapGet("/cupos", async (DpmDbContext db, CancellationToken ct) =>
        {
            var cupos = await db.CuposUrgencia.AsNoTracking().OrderBy(c => c.MedicoId).ThenBy(c => c.DiaSemana).ThenBy(c => c.Hora).ToListAsync(ct);
            return Results.Ok(cupos.Select(c => new { c.Id, medicoId = IdsDpm.Medico(c.MedicoId), diaSemana = (int)c.DiaSemana, hora = c.Hora.ToString("HH:mm") }));
        });

        panel.MapPost("/cupos", async (CupoUrgenciaRequest req, ClaimsPrincipal user, DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            if (IdsDpm.Leer(req.MedicoId, "MED") is not { } medico || !await db.Medicos.AnyAsync(m => m.Id == medico, ct))
                return Results.BadRequest(new { error = $"No existe el médico {req.MedicoId}." });
            if (req.DiaSemana is < 1 or > 5 || !TimeOnly.TryParseExact(req.Hora, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora)
                || hora < new TimeOnly(8, 0) || hora >= new TimeOnly(17, 0))
                return Results.BadRequest(new { error = "Indique un día de lunes a viernes y una hora entre 08:00 y 16:59 (HH:mm)." });
            var dia = (DayOfWeek)req.DiaSemana;
            if (await db.CuposUrgencia.AnyAsync(c => c.MedicoId == medico && c.DiaSemana == dia && c.Hora == hora, ct))
                return Results.Conflict(new { error = "Ese cupo ya existe." });
            db.CuposUrgencia.Add(new CupoUrgencia { ConsultorioId = db.ConsultorioId, MedicoId = medico, DiaSemana = dia, Hora = hora });
            await db.SaveChangesAsync(ct);
            await bitacora.RegistrarAsync(user.Actor(), "cupo_urgencia_agregado", "medico", IdsDpm.Medico(medico), null, $"{dia} {hora:HH:mm}", ct);
            return Results.Ok(new { agregado = true });
        });

        panel.MapDelete("/cupos/{id:int}", async (int id, ClaimsPrincipal user, DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            if (await db.CuposUrgencia.Where(c => c.Id == id).ExecuteDeleteAsync(ct) == 0)
                return Results.NotFound();
            await bitacora.RegistrarAsync(user.Actor(), "cupo_urgencia_quitado", "consultorio", null, null, null, ct);
            return Results.Ok(new { quitado = true });
        });

        panel.MapGet("/config", async (DpmDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Consultorios.AsNoTracking().Where(c => c.Id == db.ConsultorioId)
                .Select(c => new { horaLiberarCupos = c.HoraLiberarCuposUrgencia, centrosEmergencia = c.CentrosEmergencia }).FirstAsync(ct)));

        panel.MapPut("/config", async (ConfigUrgenciasRequest req, ClaimsPrincipal user, DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            if (req.HoraLiberarCupos is < 8 or > 17)
                return Results.BadRequest(new { error = "La hora de liberación debe estar entre 8 y 17." });
            if (req.CentrosEmergencia is { Length: > 1000 })
                return Results.BadRequest(new { error = "La lista de centros admite hasta 1000 caracteres." });
            var c = await db.Consultorios.FirstAsync(x => x.Id == db.ConsultorioId, ct);
            c.HoraLiberarCuposUrgencia = req.HoraLiberarCupos ?? c.HoraLiberarCuposUrgencia;
            if (req.CentrosEmergencia is not null)
                c.CentrosEmergencia = string.IsNullOrWhiteSpace(req.CentrosEmergencia) ? null : req.CentrosEmergencia.Trim();
            await db.SaveChangesAsync(ct);
            await bitacora.RegistrarAsync(user.Actor(), "urgencias_configuradas", "consultorio", null, null, $"liberar={c.HoraLiberarCuposUrgencia}", ct);
            return Results.Ok(new { horaLiberarCupos = c.HoraLiberarCuposUrgencia, centrosEmergencia = c.CentrosEmergencia });
        }).RequireAuthorization(PanelExtensions.PoliticaAdmin);

        return app;
    }
}
