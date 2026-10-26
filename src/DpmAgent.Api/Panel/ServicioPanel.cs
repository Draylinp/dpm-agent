using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Panel;

public sealed record CitaDelDia(
    string Id,
    DateTimeOffset FechaHora,
    string Hora,
    string PacienteId,
    string Paciente,
    string Telefono,
    string? Ars,
    string? NumeroAfiliado,
    string MedicoId,
    string Medico,
    string Motivo,
    string Estado,
    bool AsistenciaConfirmada);

/// <summary>Consultas y acciones del panel de la secretaria, siempre dentro de su consultorio.</summary>
public sealed class ServicioPanel
{
    private readonly DpmDbContext _db;
    private readonly TimeProvider _clock;

    public ServicioPanel(DpmDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<CitaDelDia>> CitasDelDiaAsync(DateOnly fecha, CancellationToken ct)
    {
        var desde = HorarioConsultorio.EnHoraRd(fecha, TimeOnly.MinValue).UtcDateTime;
        var hasta = HorarioConsultorio.EnHoraRd(fecha.AddDays(1), TimeOnly.MinValue).UtcDateTime;

        var citas = await _db.Citas.AsNoTracking()
            .Include(c => c.Paciente)
            .Include(c => c.Medico)
            .Where(c => c.FechaHoraUtc >= desde && c.FechaHoraUtc < hasta)
            .OrderBy(c => c.FechaHoraUtc)
            .ThenBy(c => c.MedicoId)
            .ToListAsync(ct);

        return citas.Select(c =>
        {
            var fechaHora = TimeZoneInfo.ConvertTime(new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero), HorarioConsultorio.ZonaRd);
            return new CitaDelDia(
                IdsDpm.Cita(c.Id), fechaHora, Agenda.TextosAgenda.Hora(fechaHora),
                IdsDpm.Paciente(c.PacienteId), c.Paciente!.Nombre, c.Paciente.Telefono, c.Paciente.Ars, c.Paciente.NumeroAfiliado,
                IdsDpm.Medico(c.MedicoId), c.Medico!.Nombre, c.Motivo, c.Estado, c.AsistenciaConfirmadaUtc is not null);
        }).ToList();
    }

    public async Task<IReadOnlyList<Escalamiento>> EscalamientosAsync(bool soloPendientes, CancellationToken ct)
    {
        var query = _db.Escalamientos.AsNoTracking();
        if (soloPendientes)
            query = query.Where(e => e.Estado == "pendiente");

        var lista = await query.OrderByDescending(e => e.Urgente).ThenByDescending(e => e.Id).Take(200).ToListAsync(ct);
        return lista.Select(e => new Escalamiento(
            IdsDpm.Escalamiento(e.Id), e.SesionId, e.PacienteId is { } p ? IdsDpm.Paciente(p) : null,
            e.Motivo, e.Urgente, e.Estado, new DateTimeOffset(e.CreadoUtc, TimeSpan.Zero), e.MotivoEn, e.MensajeOriginal, e.Idioma)).ToList();
    }

    /// <returns>false si no existe en este consultorio.</returns>
    public async Task<bool> AtenderEscalamientoAsync(string escalamientoId, string usuario, CancellationToken ct)
    {
        var id = IdsDpm.Leer(escalamientoId, "ESC");
        var e = id is null ? null : await _db.Escalamientos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null)
            return false;

        if (e.Estado != "atendido")
        {
            e.Estado = "atendido";
            e.AtendidoUtc = _clock.GetUtcNow().UtcDateTime;
            e.AtendidoPor = usuario;
            await _db.SaveChangesAsync(ct);
        }

        return true;
    }
}
