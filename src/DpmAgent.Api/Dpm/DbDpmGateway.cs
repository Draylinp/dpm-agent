using DpmAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Api.Dpm;

/// <summary>
/// Datos del consultorio guardados con EF Core (SQLite o PostgreSQL).
/// El contexto ya filtra todo por el consultorio actual.
/// </summary>
public sealed class DbDpmGateway : IDpmGateway
{
    private const string Confirmada = "confirmada";

    private readonly DpmDbContext _db;
    private readonly TimeProvider _clock;

    public DbDpmGateway(DpmDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    private DateTime AhoraUtc => _clock.GetUtcNow().UtcDateTime;

    public async Task<Paciente?> BuscarPacienteAsync(string telefonoOId, CancellationToken ct)
    {
        E.Paciente? p;
        if (IdsDpm.Leer(telefonoOId, "PAC") is { } id)
        {
            p = await _db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        }
        else
        {
            var telefono = Telefono.Normalizar(telefonoOId);
            p = telefono is null
                ? null
                : await _db.Pacientes.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(x => x.Telefono == telefono, ct);
        }

        return p is null ? null : APaciente(p);
    }

    public async Task<Paciente> RegistrarPacienteAsync(string nombre, string telefono, string? ars, CancellationToken ct)
    {
        var p = new E.Paciente
        {
            ConsultorioId = _db.ConsultorioId,
            Nombre = nombre.Trim(),
            Telefono = Telefono.Normalizar(telefono) ?? throw new ArgumentException("El teléfono no es válido."),
            Ars = string.IsNullOrWhiteSpace(ars) ? null : ars.Trim(),
            CreadoUtc = AhoraUtc,
        };

        _db.Pacientes.Add(p);
        await _db.SaveChangesAsync(ct);
        return APaciente(p);
    }

    public async Task<IReadOnlyList<Medico>> ListarMedicosAsync(CancellationToken ct)
    {
        var medicos = await _db.Medicos.AsNoTracking().Where(m => m.Activo).OrderBy(m => m.Id).ToListAsync(ct);
        return medicos.Select(m => new Medico(IdsDpm.Medico(m.Id), m.Nombre, m.Especialidad)).ToList();
    }

    public async Task<IReadOnlyList<DateTimeOffset>> ConsultarDisponibilidadAsync(string medicoId, DateOnly fecha, CancellationToken ct)
    {
        var medico = await MedicoActivoAsync(medicoId, ct);

        var turnos = HorarioConsultorio.TurnosDelDia(fecha);
        if (turnos.Count == 0)
            return turnos;

        var desde = turnos[0].UtcDateTime;
        var hasta = turnos[^1].UtcDateTime;
        var ocupadas = (await _db.Citas.AsNoTracking()
                .Where(c => c.MedicoId == medico && c.Estado == Confirmada && c.FechaHoraUtc >= desde && c.FechaHoraUtc <= hasta)
                .Select(c => c.FechaHoraUtc)
                .ToListAsync(ct))
            .ToHashSet();

        var ahora = _clock.GetUtcNow();
        return turnos.Where(t => t > ahora && !ocupadas.Contains(t.UtcDateTime)).ToList();
    }

    public async Task<Cita> CrearCitaAsync(string pacienteId, string medicoId, DateTimeOffset fechaHora, string motivo, CancellationToken ct)
    {
        var paciente = IdsDpm.Leer(pacienteId, "PAC") is { } pid && await _db.Pacientes.AnyAsync(p => p.Id == pid, ct)
            ? pid
            : throw new ArgumentException($"No existe el paciente {pacienteId}.");

        var disponibles = await ConsultarDisponibilidadAsync(medicoId, HorarioConsultorio.FechaRd(fechaHora), ct);
        if (!disponibles.Contains(fechaHora))
            throw new InvalidOperationException("Ese horario no está disponible.");

        var cita = new E.Cita
        {
            ConsultorioId = _db.ConsultorioId,
            PacienteId = paciente,
            MedicoId = IdsDpm.Leer(medicoId, "MED")!.Value,
            FechaHoraUtc = fechaHora.UtcDateTime,
            Motivo = motivo.Trim(),
            Estado = Confirmada,
            CreadaUtc = AhoraUtc,
        };

        _db.Citas.Add(cita);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // El índice único de citas confirmadas detectó una reserva simultánea.
            _db.Entry(cita).State = EntityState.Detached;
            throw new InvalidOperationException("Ese horario se acaba de ocupar.");
        }

        return ACita(cita);
    }

    public async Task<IReadOnlyList<Cita>> CitasDePacienteAsync(string pacienteId, CancellationToken ct)
    {
        if (IdsDpm.Leer(pacienteId, "PAC") is not { } id)
            return [];

        var citas = await _db.Citas.AsNoTracking()
            .Where(c => c.PacienteId == id && c.Estado == Confirmada)
            .OrderBy(c => c.FechaHoraUtc)
            .ToListAsync(ct);

        return citas.Select(ACita).ToList();
    }

    public async Task<Cita?> CancelarCitaAsync(string citaId, string pacienteId, CancellationToken ct)
    {
        if (IdsDpm.Leer(citaId, "CITA") is not { } id || IdsDpm.Leer(pacienteId, "PAC") is not { } pid)
            return null;

        var cita = await _db.Citas.FirstOrDefaultAsync(c => c.Id == id && c.PacienteId == pid, ct);
        if (cita is null)
            return null;

        if (cita.Estado != "cancelada")
        {
            cita.Estado = "cancelada";
            cita.CanceladaUtc = AhoraUtc;
            await _db.SaveChangesAsync(ct);
        }

        return ACita(cita);
    }

    public async Task<Escalamiento> RegistrarEscalamientoAsync(
        string sesionId, string? pacienteId, string motivo, bool urgente, CancellationToken ct)
    {
        var e = new E.Escalamiento
        {
            ConsultorioId = _db.ConsultorioId,
            SesionId = sesionId,
            PacienteId = IdsDpm.Leer(pacienteId, "PAC"),
            Motivo = motivo.Trim(),
            Urgente = urgente,
            Estado = "pendiente",
            CreadoUtc = AhoraUtc,
        };

        _db.Escalamientos.Add(e);
        await _db.SaveChangesAsync(ct);
        return AEscalamiento(e);
    }

    public async Task<IReadOnlyList<Escalamiento>> ListarEscalamientosAsync(CancellationToken ct)
    {
        var lista = await _db.Escalamientos.AsNoTracking().OrderByDescending(e => e.Id).ToListAsync(ct);
        return lista.Select(AEscalamiento).ToList();
    }

    private async Task<int> MedicoActivoAsync(string medicoId, CancellationToken ct) =>
        IdsDpm.Leer(medicoId, "MED") is { } id && await _db.Medicos.AnyAsync(m => m.Id == id && m.Activo, ct)
            ? id
            : throw new ArgumentException($"No existe el médico {medicoId}.");

    private static Paciente APaciente(E.Paciente p) => new(IdsDpm.Paciente(p.Id), p.Nombre, p.Telefono, p.Ars);

    private static Cita ACita(E.Cita c) => new(
        IdsDpm.Cita(c.Id),
        IdsDpm.Paciente(c.PacienteId),
        IdsDpm.Medico(c.MedicoId),
        TimeZoneInfo.ConvertTime(new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero), HorarioConsultorio.ZonaRd),
        c.Motivo,
        c.Estado);

    private static Escalamiento AEscalamiento(E.Escalamiento e) => new(
        IdsDpm.Escalamiento(e.Id),
        e.SesionId,
        e.PacienteId is { } p ? IdsDpm.Paciente(p) : null,
        e.Motivo,
        e.Urgente,
        e.Estado,
        new DateTimeOffset(e.CreadoUtc, TimeSpan.Zero));
}
