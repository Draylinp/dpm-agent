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
                : await _db.Pacientes.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(x => x.Telefono == telefono && x.Activo, ct);
        }

        return p is null ? null : APaciente(p);
    }

    public async Task<IReadOnlyList<Paciente>> PacientesDelTelefonoAsync(string telefono, CancellationToken ct)
    {
        if (Telefono.Normalizar(telefono) is not { } normalizado)
            return [];
        // Un paciente desactivado ya no se identifica por el número (pudo pasar a otra persona).
        var lista = await _db.Pacientes.AsNoTracking().Where(x => x.Telefono == normalizado && x.Activo).OrderBy(x => x.Id).ToListAsync(ct);
        return lista.Select(APaciente).ToList();
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
        if (turnos.Count == 0 || await NoLaborableAsync(medico, fecha, ct))
            return [];

        var desde = turnos[0].UtcDateTime;
        var hasta = turnos[^1].UtcDateTime;
        var ocupadas = (await _db.Citas.AsNoTracking()
                .Where(c => c.MedicoId == medico && c.Estado == Confirmada && c.FechaHoraUtc >= desde && c.FechaHoraUtc <= hasta)
                .Select(c => c.FechaHoraUtc)
                .ToListAsync(ct))
            .ToHashSet();

        var ahora = _clock.GetUtcNow();
        var reservados = await CuposUrgenciaReservadosAsync(medico, fecha, ahora, ct);
        return turnos.Where(t => t > ahora && !ocupadas.Contains(t.UtcDateTime) && !reservados.Contains(TimeOnly.FromDateTime(t.DateTime))).ToList();
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

    public async Task<Escalamiento> RegistrarEscalamientoAsync(NuevoEscalamiento nuevo, CancellationToken ct)
    {
        var e = new E.Escalamiento
        {
            ConsultorioId = _db.ConsultorioId,
            SesionId = nuevo.SesionId,
            PacienteId = IdsDpm.Leer(nuevo.PacienteId, "PAC"),
            Motivo = Recortar(nuevo.ResumenEs.Trim(), 1000),
            MotivoEn = nuevo.ResumenEn is null ? null : Recortar(nuevo.ResumenEn.Trim(), 1000),
            MensajeOriginal = nuevo.MensajeOriginal is null ? null : Recortar(nuevo.MensajeOriginal, 2000),
            Idioma = nuevo.Idioma,
            Urgente = nuevo.Urgente,
            Estado = "pendiente",
            CreadoUtc = AhoraUtc,
        };

        _db.Escalamientos.Add(e);
        await _db.SaveChangesAsync(ct);
        return AEscalamiento(e);
    }

    public async Task<InfoConsultorio> ObtenerConsultorioAsync(CancellationToken ct)
    {
        var c = await _db.Consultorios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == _db.ConsultorioId, ct);
        return c is null
            ? new InfoConsultorio("", "911", "es")
            : new InfoConsultorio(c.Nombre, c.TelefonoEmergencias, c.IdiomaPredeterminado, c.NombreAsistente);
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task<IReadOnlyList<Escalamiento>> ListarEscalamientosAsync(CancellationToken ct)
    {
        var lista = await _db.Escalamientos.AsNoTracking().OrderByDescending(e => e.Id).ToListAsync(ct);
        return lista.Select(AEscalamiento).ToList();
    }

    public async Task<Paciente> ActualizarSeguroAsync(string pacienteId, string? ars, string? numeroAfiliado, CancellationToken ct)
    {
        var p = IdsDpm.Leer(pacienteId, "PAC") is { } id
            ? await _db.Pacientes.FirstOrDefaultAsync(x => x.Id == id, ct)
            : null;
        if (p is null)
            throw new ArgumentException($"No existe el paciente {pacienteId}.");

        p.Ars = ars ?? p.Ars;
        p.NumeroAfiliado = numeroAfiliado ?? p.NumeroAfiliado;
        await _db.SaveChangesAsync(ct);
        return APaciente(p);
    }

    public async Task<CarnetLeido> GuardarCarnetPendienteAsync(
        string sesionId, string telefonoCanal, string? pacienteId, DatosCarnet datos, CancellationToken ct)
    {
        var c = new E.CarnetPendiente
        {
            ConsultorioId = _db.ConsultorioId,
            SesionId = sesionId,
            TelefonoCanal = telefonoCanal,
            PacienteId = IdsDpm.Leer(pacienteId, "PAC"),
            Nombre = datos.Nombre,
            Ars = datos.Ars,
            NumeroAfiliado = datos.NumeroAfiliado,
            Estado = "pendiente",
            CreadoUtc = AhoraUtc,
        };

        _db.CarnetsPendientes.Add(c);
        await _db.SaveChangesAsync(ct);
        return ACarnet(c);
    }

    public async Task<CarnetLeido?> ObtenerCarnetAsync(string carnetId, CancellationToken ct)
    {
        if (IdsDpm.Leer(carnetId, "CAR") is not { } id)
            return null;

        var c = await _db.CarnetsPendientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : ACarnet(c);
    }

    public async Task<CarnetLeido?> UltimoCarnetPendienteAsync(string sesionId, CancellationToken ct)
    {
        var c = await _db.CarnetsPendientes.AsNoTracking()
            .Where(x => x.SesionId == sesionId && x.Estado == "pendiente")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return c is null ? null : ACarnet(c);
    }

    public async Task<IReadOnlyList<CarnetLeido>> ListarCarnetsPendientesAsync(CancellationToken ct)
    {
        var lista = await _db.CarnetsPendientes.AsNoTracking()
            .Where(x => x.Estado == "pendiente")
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        return lista.Select(ACarnet).ToList();
    }

    public async Task<CarnetLeido> ResolverCarnetAsync(
        string carnetId, string estado, DatosCarnet datos, string? pacienteId, string resueltoPor, CancellationToken ct)
    {
        var c = IdsDpm.Leer(carnetId, "CAR") is { } id
            ? await _db.CarnetsPendientes.FirstOrDefaultAsync(x => x.Id == id, ct)
            : null;
        if (c is null)
            throw new ArgumentException($"No existe el carnet {carnetId}.");
        if (c.Estado != "pendiente")
            throw new InvalidOperationException($"El carnet {IdsDpm.Carnet(c.Id)} ya fue {c.Estado}.");

        c.Estado = estado;
        c.Nombre = datos.Nombre;
        c.Ars = datos.Ars;
        c.NumeroAfiliado = datos.NumeroAfiliado;
        c.PacienteId = IdsDpm.Leer(pacienteId, "PAC") ?? c.PacienteId;
        c.ResueltoPor = resueltoPor;
        c.ResueltoUtc = AhoraUtc;

        try
        {
            // Estado es token de concurrencia: si otra petición lo resolvió primero, esto falla.
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException($"El carnet {IdsDpm.Carnet(c.Id)} ya fue resuelto por otra persona.");
        }

        return ACarnet(c);
    }

    /// <summary>
    /// Cupos de urgencia que todavía no se liberan para citas normales: los del día de la semana, salvo que ya sea
    /// ese mismo día y pasó la hora de liberación del consultorio.
    /// </summary>
    private async Task<HashSet<TimeOnly>> CuposUrgenciaReservadosAsync(int medico, DateOnly fecha, DateTimeOffset ahora, CancellationToken ct)
    {
        var dia = fecha.DayOfWeek;
        var cupos = await _db.CuposUrgencia.AsNoTracking().Where(c => c.MedicoId == medico && c.DiaSemana == dia).Select(c => c.Hora).ToListAsync(ct);
        if (cupos.Count == 0)
            return [];
        var hora = await _db.Consultorios.AsNoTracking().Where(c => c.Id == _db.ConsultorioId).Select(c => c.HoraLiberarCuposUrgencia).FirstAsync(ct);
        var local = TimeZoneInfo.ConvertTime(ahora, HorarioConsultorio.ZonaRd);
        return DateOnly.FromDateTime(local.DateTime) == fecha && local.Hour >= hora ? [] : cupos.ToHashSet();
    }

    /// <summary>Feriado del consultorio o día bloqueado en la agenda del médico.</summary>
    private async Task<bool> NoLaborableAsync(int medico, DateOnly fecha, CancellationToken ct) =>
        await _db.Feriados.AnyAsync(f => f.Fecha == fecha, ct) ||
        await _db.BloqueosAgenda.AnyAsync(b => b.MedicoId == medico && b.Desde <= fecha && fecha <= b.Hasta, ct);

    private async Task<int> MedicoActivoAsync(string medicoId, CancellationToken ct) =>
        IdsDpm.Leer(medicoId, "MED") is { } id && await _db.Medicos.AnyAsync(m => m.Id == id && m.Activo, ct)
            ? id
            : throw new ArgumentException($"No existe el médico {medicoId}.");

    private static Paciente APaciente(E.Paciente p) =>
        new(IdsDpm.Paciente(p.Id), p.Nombre, p.Telefono, p.Ars, p.NumeroAfiliado);

    private static CarnetLeido ACarnet(E.CarnetPendiente c) => new(
        IdsDpm.Carnet(c.Id),
        c.SesionId,
        c.TelefonoCanal,
        c.PacienteId is { } p ? IdsDpm.Paciente(p) : null,
        new DatosCarnet(c.Nombre, c.Ars, c.NumeroAfiliado),
        c.Estado,
        new DateTimeOffset(c.CreadoUtc, TimeSpan.Zero),
        c.ResueltoPor,
        c.ResueltoUtc is { } r ? new DateTimeOffset(r, TimeSpan.Zero) : null);

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
        new DateTimeOffset(e.CreadoUtc, TimeSpan.Zero),
        e.MotivoEn,
        e.MensajeOriginal,
        e.Idioma);
}
