using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Idiomas;
using DpmAgent.Api.Medicos;
using Microsoft.EntityFrameworkCore;
using Cita = DpmAgent.Api.Dpm.Cita;
using CitaDb = DpmAgent.Api.Data.Entidades.Cita;

namespace DpmAgent.Api.Agenda;

public sealed record OpcionUrgente(string MedicoId, string Medico, DateTimeOffset FechaHora, bool CupoUrgencia);

/// <param name="Solicitud">Id de la solicitud de sobrecupo creada si no había hueco (SOB-...).</param>
public sealed record ResultadoUrgente(bool FueraDeHorario, IReadOnlyList<DateOnly> Dias, IReadOnlyList<OpcionUrgente> Opciones, string? Solicitud);

public sealed record SobrecupoInfo(
    string Id, string PacienteId, string Paciente, string MedicoId, string Medico, DateOnly Fecha, string Estado, DateTimeOffset Creada,
    DateTimeOffset? HoraAprobada, string? DecididaPor);

/// <summary>
/// "Necesito que me vean lo antes posible". Sin preguntar síntomas ni juzgar la gravedad: se ofrece el primer hueco de hoy
/// o del siguiente día hábil (incluidos los cupos de urgencia) y, si no hay, se crea una solicitud de sobrecupo que deciden
/// la secretaria o el médico. El agente nunca la aprueba. Las emergencias van siempre al número de emergencias.
/// </summary>
public sealed class ServicioUrgencias(
    DpmDbContext db, IDpmGateway dpm, Notificador notificador, Bitacora bitacora, ServicioMedicos medicos, TimeProvider clock)
{
    public const string Pendiente = "pendiente";
    public const string Aprobada = "aprobada";
    public const string Rechazada = "rechazada";

    private DateTimeOffset Ahora => clock.GetUtcNow();

    /// <summary>Fuera del horario de atención: antes de las 8, después de las 17, fin de semana o feriado.</summary>
    public async Task<bool> FueraDeHorarioAsync(CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(Ahora, HorarioConsultorio.ZonaRd);
        var hoy = DateOnly.FromDateTime(local.DateTime);
        return local.Hour < 8 || local.Hour >= 17 || !await DiaDeConsultaAsync(hoy, ct);
    }

    private async Task<bool> DiaDeConsultaAsync(DateOnly dia, CancellationToken ct) =>
        HorarioConsultorio.TurnosDelDia(dia).Count > 0 && !await db.Feriados.AnyAsync(f => f.Fecha == dia, ct);

    /// <summary>Centros de emergencia que configuró el consultorio, uno por línea.</summary>
    public async Task<IReadOnlyList<string>> CentrosAsync(CancellationToken ct)
    {
        var texto = await db.Consultorios.AsNoTracking().Where(c => c.Id == db.ConsultorioId).Select(c => c.CentrosEmergencia).FirstOrDefaultAsync(ct);
        return (texto ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public async Task<DateOnly> SiguienteDiaHabilAsync(DateOnly desde, CancellationToken ct)
    {
        var d = desde.AddDays(1);
        while (!await DiaDeConsultaAsync(d, ct))
            d = d.AddDays(1);
        return d;
    }

    public async Task<ResultadoUrgente> PedirAsync(string pacienteId, string? medicoId, string sesionId, CancellationToken ct)
    {
        var paciente = IdsDpm.Leer(pacienteId, "PAC") ?? throw new ArgumentException("Primero hay que identificar al paciente.");
        var hoy = HorarioConsultorio.FechaRd(Ahora);
        var fuera = await FueraDeHorarioAsync(ct);
        var siguiente = await SiguienteDiaHabilAsync(hoy, ct);
        List<DateOnly> dias = fuera ? [siguiente] : [hoy, siguiente];

        var opciones = await OpcionesAsync(dias, medicoId, ct);
        string? solicitud = null;
        if (opciones.Count == 0)
        {
            var medico = medicoId is not null ? IdsDpm.Leer(medicoId, "MED")!.Value
                : await db.Medicos.Where(m => m.Activo).OrderBy(m => m.Id).Select(m => m.Id).FirstAsync(ct);
            solicitud = await CrearSolicitudAsync(paciente, medico, dias[0], sesionId, ct);
        }

        await bitacora.RegistrarAsync(Bitacora.Agente, "pedido_urgente", "paciente", pacienteId, sesionId,
            solicitud is null ? $"{opciones.Count} opciones" : $"sin hueco: {solicitud}", ct);
        return new ResultadoUrgente(fuera, dias, opciones, solicitud);
    }

    /// <summary>Primeros huecos de esos días (normales y cupos de urgencia), como máximo 3.</summary>
    public async Task<List<OpcionUrgente>> OpcionesAsync(IEnumerable<DateOnly> dias, string? medicoId, CancellationToken ct)
    {
        var medicosActivos = await db.Medicos.AsNoTracking().Where(m => m.Activo).OrderBy(m => m.Id).ToListAsync(ct);
        if (medicoId is not null)
            medicosActivos = medicosActivos.Where(m => m.Id == IdsDpm.Leer(medicoId, "MED")).ToList();

        var opciones = new List<OpcionUrgente>();
        foreach (var dia in dias)
        {
            foreach (var m in medicosActivos)
            {
                foreach (var t in await dpm.ConsultarDisponibilidadAsync(IdsDpm.Medico(m.Id), dia, ct))
                    opciones.Add(new OpcionUrgente(IdsDpm.Medico(m.Id), m.Nombre, t, false));
                foreach (var t in await CuposLibresAsync(m.Id, dia, ct))
                    opciones.Add(new OpcionUrgente(IdsDpm.Medico(m.Id), m.Nombre, t, true));
            }

            if (opciones.Count > 0)
                break; // hoy primero; el día siguiente solo si hoy no hay nada
        }

        return opciones.OrderBy(o => o.FechaHora).DistinctBy(o => (o.MedicoId, o.FechaHora)).Take(3).ToList();
    }

    /// <summary>Cupos de urgencia de ese día que nadie ocupó y que todavía no pasaron.</summary>
    private async Task<List<DateTimeOffset>> CuposLibresAsync(int medico, DateOnly dia, CancellationToken ct)
    {
        if (!await DiaDeConsultaAsync(dia, ct) || await db.BloqueosAgenda.AnyAsync(b => b.MedicoId == medico && b.Desde <= dia && dia <= b.Hasta, ct))
            return [];
        var horas = await db.CuposUrgencia.AsNoTracking().Where(c => c.MedicoId == medico && c.DiaSemana == dia.DayOfWeek).Select(c => c.Hora).ToListAsync(ct);
        var momentos = horas.Select(h => HorarioConsultorio.EnHoraRd(dia, h)).Where(t => t > Ahora).ToList();
        var utc = momentos.Select(m => m.UtcDateTime).ToList();
        var ocupadas = await db.Citas.AsNoTracking().Where(c => c.MedicoId == medico && c.Estado == "confirmada" && utc.Contains(c.FechaHoraUtc))
            .Select(c => c.FechaHoraUtc).ToListAsync(ct);
        return momentos.Where(m => !ocupadas.Contains(m.UtcDateTime)).ToList();
    }

    /// <summary>Reserva una de las opciones urgentes (un hueco normal o un cupo de urgencia) para el paciente.</summary>
    public async Task<Cita> ReservarAsync(string pacienteId, string medicoId, DateTimeOffset fechaHora, CancellationToken ct)
    {
        var dias = new List<DateOnly> { HorarioConsultorio.FechaRd(Ahora) };
        dias.Add(await SiguienteDiaHabilAsync(dias[0], ct));
        var opciones = new List<OpcionUrgente>();
        foreach (var dia in dias)
            opciones.AddRange(await OpcionesTodasAsync(dia, medicoId, ct));
        var elegida = opciones.FirstOrDefault(o => o.FechaHora == fechaHora)
                      ?? throw new InvalidOperationException("Ese horario ya no está disponible. Vuelve a usar pedir_cita_urgente.");

        if (!elegida.CupoUrgencia)
        {
            var normal = await dpm.CrearCitaAsync(pacienteId, medicoId, fechaHora, "consulta general", ct);
            var creada = await db.Citas.FirstAsync(c => c.Id == IdsDpm.Leer(normal.Id, "CITA"), ct);
            creada.Urgente = true; // pedida como urgente: prioridad si hay que reprogramarla
            await db.SaveChangesAsync(ct);
            return normal;
        }

        var cita = new CitaDb
        {
            ConsultorioId = db.ConsultorioId,
            PacienteId = IdsDpm.Leer(pacienteId, "PAC")!.Value,
            MedicoId = IdsDpm.Leer(medicoId, "MED")!.Value,
            FechaHoraUtc = fechaHora.UtcDateTime,
            Motivo = "consulta general",
            Estado = "confirmada",
            Urgente = true,
            CreadaUtc = Ahora.UtcDateTime,
        };
        db.Citas.Add(cita);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(cita).State = EntityState.Detached;
            throw new InvalidOperationException("Ese horario se acaba de ocupar.");
        }

        return new Cita(IdsDpm.Cita(cita.Id), pacienteId, medicoId, fechaHora, cita.Motivo, cita.Estado);
    }

    private async Task<List<OpcionUrgente>> OpcionesTodasAsync(DateOnly dia, string medicoId, CancellationToken ct)
    {
        var id = IdsDpm.Leer(medicoId, "MED") ?? throw new ArgumentException($"No existe el médico {medicoId}.");
        var nombre = await db.Medicos.Where(m => m.Id == id).Select(m => m.Nombre).FirstAsync(ct);
        var lista = (await dpm.ConsultarDisponibilidadAsync(medicoId, dia, ct)).Select(t => new OpcionUrgente(medicoId, nombre, t, false)).ToList();
        lista.AddRange((await CuposLibresAsync(id, dia, ct)).Select(t => new OpcionUrgente(medicoId, nombre, t, true)));
        return lista;
    }

    private async Task<string> CrearSolicitudAsync(int paciente, int medico, DateOnly fecha, string sesionId, CancellationToken ct)
    {
        // Una sola solicitud abierta por paciente y día.
        var existente = await db.SolicitudesSobrecupo.FirstOrDefaultAsync(s => s.PacienteId == paciente && s.Fecha == fecha && s.Estado == Pendiente, ct);
        if (existente is not null)
            return Id(existente.Id);

        var s = new SolicitudSobrecupo
        {
            ConsultorioId = db.ConsultorioId, PacienteId = paciente, MedicoId = medico, Fecha = fecha,
            SesionId = sesionId, Estado = Pendiente, CreadaUtc = Ahora.UtcDateTime,
        };
        db.SolicitudesSobrecupo.Add(s);
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(Bitacora.Agente, "sobrecupo_solicitado", "sobrecupo", Id(s.Id), sesionId, IdsDpm.Paciente(paciente), ct);

        // Aviso inmediato al médico por su canal (si tiene número verificado y aceptó recibir avisos).
        var m = await db.Medicos.AsNoTracking().FirstAsync(x => x.Id == medico, ct);
        if (m.Telefono is not null && m.TelefonoVerificadoUtc is not null && await medicos.ResumenVigenteAsync(medico, ct))
        {
            var consultorio = await db.Consultorios.AsNoTracking().Where(c => c.Id == db.ConsultorioId).Select(c => c.Nombre).FirstAsync(ct);
            var nombre = await db.Pacientes.AsNoTracking().Where(p => p.Id == paciente).Select(p => p.Nombre).FirstAsync(ct);
            string[] p = [consultorio, m.Nombre, nombre, Textos.Dia(HorarioConsultorio.EnHoraRd(fecha, new TimeOnly(12, 0)), "es"), Id(s.Id)];
            await notificador.NotificarMedicoAsync(m.Telefono, new MensajeAgenda(Textos.Obtener("agenda.sobrecupo_medico", "es", p), "sobrecupo_medico", p, "es"),
                "sobrecupo", ct);
        }

        return Id(s.Id);
    }

    public async Task<IReadOnlyList<SobrecupoInfo>> ListarAsync(string? estado, int? medicoId, CancellationToken ct)
    {
        var q = db.SolicitudesSobrecupo.AsNoTracking().AsQueryable();
        if (estado is not null)
            q = q.Where(s => s.Estado == estado);
        if (medicoId is { } mid)
            q = q.Where(s => s.MedicoId == mid);
        var lista = await q.OrderBy(s => s.Id).Take(200).ToListAsync(ct);
        var pacientes = await db.Pacientes.AsNoTracking().Where(p => lista.Select(s => s.PacienteId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        var nombres = await db.Medicos.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);
        return lista.Select(s => new SobrecupoInfo(Id(s.Id), IdsDpm.Paciente(s.PacienteId), pacientes.GetValueOrDefault(s.PacienteId, "?"),
            IdsDpm.Medico(s.MedicoId), nombres.GetValueOrDefault(s.MedicoId, "?"), s.Fecha, s.Estado, new DateTimeOffset(s.CreadaUtc, TimeSpan.Zero),
            s.HoraAprobadaUtc is { } h ? new DateTimeOffset(h, TimeSpan.Zero) : null, s.DecididaPor)).ToList();
    }

    /// <summary>
    /// La secretaria o el médico deciden. Si se aprueba, se crea la cita a la hora indicada (puede quedar fuera de los turnos
    /// normales) y se le avisa al paciente; si se rechaza, se le ofrece buscar el próximo horario.
    /// </summary>
    public async Task<SobrecupoInfo> DecidirAsync(string solicitudId, bool aprobar, TimeOnly? hora, string actor, int? soloMedico, CancellationToken ct)
    {
        var id = IdsDpm.Leer(solicitudId, "SOB") ?? throw new ArgumentException($"No existe la solicitud {solicitudId}.");
        var s = await db.SolicitudesSobrecupo.FirstOrDefaultAsync(x => x.Id == id && (soloMedico == null || x.MedicoId == soloMedico), ct)
                ?? throw new ArgumentException($"No existe la solicitud {solicitudId}.");
        if (s.Estado != Pendiente)
            throw new InvalidOperationException($"La solicitud {solicitudId} ya está {s.Estado}.");

        var consultorio = await db.Consultorios.AsNoTracking().Where(c => c.Id == db.ConsultorioId).Select(c => new { c.Nombre, c.TelefonoEmergencias }).FirstAsync(ct);
        var paciente = await db.Pacientes.AsNoTracking().FirstAsync(p => p.Id == s.PacienteId, ct);
        var medico = await db.Medicos.AsNoTracking().FirstAsync(m => m.Id == s.MedicoId, ct);

        if (aprobar)
        {
            if (hora is not { } h)
                throw new ArgumentException("Para aprobar, indique la hora (HH:mm).");
            var momento = HorarioConsultorio.EnHoraRd(s.Fecha, h);
            if (momento <= Ahora)
                throw new ArgumentException("La hora ya pasó.");
            if (await db.Citas.AnyAsync(c => c.MedicoId == s.MedicoId && c.FechaHoraUtc == momento.UtcDateTime && c.Estado == "confirmada", ct))
                throw new InvalidOperationException("Ya hay una cita a esa hora; indique otra (por ejemplo, unos minutos después).");

            var cita = new CitaDb
            {
                ConsultorioId = db.ConsultorioId, PacienteId = s.PacienteId, MedicoId = s.MedicoId, FechaHoraUtc = momento.UtcDateTime,
                Motivo = "consulta general", Estado = "confirmada", Urgente = true, CreadaUtc = Ahora.UtcDateTime,
            };
            db.Citas.Add(cita);
            s.Estado = Aprobada;
            s.HoraAprobadaUtc = momento.UtcDateTime;
            await Cerrar(s, actor, ct);
            await notificador.NotificarPacienteAsync(s.PacienteId, (idioma, cons) =>
            {
                var i = Idiomas.Idiomas.ParaTextos(idioma);
                string[] p = [cons, Primer(paciente.Nombre), medico.Nombre, Textos.Dia(momento, i), Textos.Hora(momento, i), IdsDpm.Cita(cita.Id), consultorio.TelefonoEmergencias];
                return new MensajeAgenda(Textos.Obtener("agenda.sobrecupo_aprobado", i, p), "sobrecupo_aprobado", p, i);
            }, "sobrecupo", ct);
        }
        else
        {
            s.Estado = Rechazada;
            await Cerrar(s, actor, ct);
            await notificador.NotificarPacienteAsync(s.PacienteId, (idioma, cons) =>
            {
                var i = Idiomas.Idiomas.ParaTextos(idioma);
                string[] p = [cons, Primer(paciente.Nombre), Textos.Dia(HorarioConsultorio.EnHoraRd(s.Fecha, new TimeOnly(12, 0)), i), consultorio.TelefonoEmergencias];
                return new MensajeAgenda(Textos.Obtener("agenda.sobrecupo_rechazado", i, p), "sobrecupo_rechazado", p, i);
            }, "sobrecupo", ct);
        }

        return (await ListarAsync(null, null, ct)).Single(x => x.Id == Id(s.Id));
    }

    private async Task Cerrar(SolicitudSobrecupo s, string actor, CancellationToken ct)
    {
        s.DecididaPor = actor.Length > 60 ? actor[..60] : actor;
        s.DecididaUtc = Ahora.UtcDateTime;
        s.Comunicada = true;
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, s.Estado == Aprobada ? "sobrecupo_aprobado" : "sobrecupo_rechazado", "sobrecupo", Id(s.Id), null, null, ct);
    }

    private static string Primer(string nombre) => nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombre;

    public static string Id(int id) => $"SOB-{id:000}";
}
