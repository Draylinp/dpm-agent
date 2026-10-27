using DpmAgent.Api.Agent;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Agenda;

/// <param name="ToleranciaDias">Días antes o después de la fecha que el médico acepta (0 a 30).</param>
/// <param name="RecurrenciaUnidad">semanas o meses.</param>
public sealed record NuevoSeguimiento(
    string PacienteId, string MedicoId, DateOnly FechaObjetivo, int ToleranciaDias, string? Motivo = null,
    int? RecurrenciaCada = null, string? RecurrenciaUnidad = null);

public sealed record SeguimientoInfo(
    string Id, string PacienteId, string Paciente, string MedicoId, string Medico, DateOnly FechaObjetivo, int ToleranciaDias,
    DateOnly VentanaDesde, DateOnly VentanaHasta, string Motivo, string? Recurrencia, string Estado, int Intentos, bool PorLlamada,
    string? CitaId, DateTimeOffset? CitaFechaHora);

public sealed record ResultadoSeguimientos(int Contactos, int Reintentos, int SinRespuesta, int Recordatorios, int Recurrencias, int Reabiertos);

/// <summary>
/// Seguimientos indicados por el médico. Todo es administrativo: el médico decide la fecha y la tolerancia;
/// el sistema agenda dentro de esa ventana o contacta al paciente para que escoja. Nunca evalúa nada clínico.
/// Los mensajes usan la categoría de consentimiento "seguimientos": sin permiso, pasan a "Llamadas pendientes".
/// </summary>
public sealed class ServicioSeguimientos
{
    public const string Pendiente = "pendiente";
    public const string Contactando = "contactando";
    public const string Agendado = "agendado";
    public const string SinRespuesta = "sin_respuesta";
    public const string Cancelado = "cancelado";

    private readonly DpmDbContext _db;
    private readonly IDpmGateway _dpm;
    private readonly Notificador _notificador;
    private readonly Bitacora _bitacora;
    private readonly AgendaOptions _opciones;
    private readonly TimeProvider _clock;

    public ServicioSeguimientos(
        DpmDbContext db, IDpmGateway dpm, Notificador notificador, Bitacora bitacora, IOptions<AgendaOptions> opciones, TimeProvider clock)
    {
        _db = db;
        _dpm = dpm;
        _notificador = notificador;
        _bitacora = bitacora;
        _opciones = opciones.Value;
        _clock = clock;
    }

    private DateTime AhoraUtc => _clock.GetUtcNow().UtcDateTime;

    private DateOnly Hoy => HorarioConsultorio.FechaRd(_clock.GetUtcNow());

    public async Task<SeguimientoInfo> CrearAsync(NuevoSeguimiento n, string creadoPor, CancellationToken ct)
    {
        var paciente = IdsDpm.Leer(n.PacienteId, "PAC") is { } p && await _db.Pacientes.AnyAsync(x => x.Id == p, ct)
            ? p : throw new ArgumentException($"No existe el paciente {n.PacienteId}.");
        var medico = IdsDpm.Leer(n.MedicoId, "MED") is { } m && await _db.Medicos.AnyAsync(x => x.Id == m && x.Activo, ct)
            ? m : throw new ArgumentException($"No existe el médico {n.MedicoId}.");
        if (n.ToleranciaDias is < 0 or > 30)
            throw new ArgumentException("La tolerancia debe estar entre 0 y 30 días.");
        if (n.FechaObjetivo <= Hoy)
            throw new ArgumentException("La fecha del seguimiento debe ser futura.");
        if (n.FechaObjetivo > Hoy.AddYears(3))
            throw new ArgumentException("La fecha del seguimiento no puede pasar de 3 años.");
        var unidad = n.RecurrenciaUnidad?.Trim().ToLowerInvariant();
        if (n.RecurrenciaCada is { } cada && (cada is < 1 or > 24 || unidad is not ("semanas" or "meses")))
            throw new ArgumentException("La recurrencia es cada 1 a 24 semanas o meses.");

        var seg = new Seguimiento
        {
            ConsultorioId = _db.ConsultorioId,
            PacienteId = paciente,
            MedicoId = medico,
            FechaObjetivo = n.FechaObjetivo,
            ToleranciaDias = n.ToleranciaDias,
            Motivo = n.Motivo is null ? "seguimiento" : AgentTools.MotivoAdministrativo(n.Motivo),
            RecurrenciaCada = n.RecurrenciaCada,
            RecurrenciaUnidad = n.RecurrenciaCada is null ? null : unidad,
            Estado = Pendiente,
            CreadoPor = creadoPor.Length > 60 ? creadoPor[..60] : creadoPor,
            CreadoUtc = AhoraUtc,
        };
        _db.Seguimientos.Add(seg);
        await _db.SaveChangesAsync(ct);
        await _bitacora.RegistrarAsync(creadoPor, "seguimiento_creado", "seguimiento", Id(seg.Id), null,
            $"{IdsDpm.Paciente(paciente)} con {IdsDpm.Medico(medico)} hacia {seg.FechaObjetivo:yyyy-MM-dd} ±{seg.ToleranciaDias}d", ct);

        var horizonte = await _db.Consultorios.Where(c => c.Id == _db.ConsultorioId).Select(c => c.HorizonteSeguimientoSemanas).FirstAsync(ct);
        if (seg.FechaObjetivo <= Hoy.AddDays(7 * horizonte))
        {
            // Dentro del horizonte: se agenda de una vez en el primer hueco de la ventana y se confirma con el paciente.
            if (!await AgendarEnPrimerHuecoAsync(seg, ct))
                await AbrirContactoAsync(seg, ct);
        }
        else if (_opciones.SeguimientoAvisarAlCrear)
        {
            var (nombre, medicoNombre) = await NombresAsync(seg, ct);
            await _notificador.NotificarPacienteAsync(seg.PacienteId,
                (idioma, consultorio) => TextosAgenda.SeguimientoCreado(idioma, consultorio, nombre, medicoNombre, seg.FechaObjetivo), "seguimiento", ct);
        }

        return (await ListarAsync(null, ct, seg.Id)).Single();
    }

    /// <summary>Primer día y hora libres dentro de la ventana (respeta feriados, bloqueos y la agenda).</summary>
    public async Task<DateTimeOffset?> PrimerHuecoAsync(int medicoId, DateOnly objetivo, int tolerancia, CancellationToken ct)
    {
        var desde = objetivo.AddDays(-tolerancia);
        if (desde <= Hoy)
            desde = Hoy.AddDays(1);
        for (var d = desde; d <= objetivo.AddDays(tolerancia); d = d.AddDays(1))
        {
            var libres = await _dpm.ConsultarDisponibilidadAsync(IdsDpm.Medico(medicoId), d, ct);
            if (libres.Count > 0)
                return libres[0];
        }

        return null;
    }

    private async Task<bool> AgendarEnPrimerHuecoAsync(Seguimiento seg, CancellationToken ct)
    {
        var hueco = await PrimerHuecoAsync(seg.MedicoId, seg.FechaObjetivo, seg.ToleranciaDias, ct);
        if (hueco is not { } fecha)
            return false;

        var cita = await _dpm.CrearCitaAsync(IdsDpm.Paciente(seg.PacienteId), IdsDpm.Medico(seg.MedicoId), fecha, seg.Motivo, ct);
        await VincularAsync(seg, IdsDpm.Leer(cita.Id, "CITA")!.Value, ct);
        await _bitacora.RegistrarAsync(Bitacora.Sistema, "cita_creada", "cita", cita.Id, null, $"seguimiento {Id(seg.Id)}", ct);

        var (nombre, medico) = await NombresAsync(seg, ct);
        var r = await _notificador.NotificarPacienteAsync(seg.PacienteId,
            (idioma, consultorio) => TextosAgenda.SeguimientoAgendado(idioma, consultorio, nombre, medico, fecha, cita.Id), "seguimiento", ct);
        seg.PorLlamada = !r.Enviado;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task VincularAsync(Seguimiento seg, int citaId, CancellationToken ct)
    {
        var cita = await _db.Citas.FirstAsync(c => c.Id == citaId, ct);
        cita.SeguimientoId = seg.Id;
        seg.CitaId = citaId;
        seg.Estado = Agendado;
        await _db.SaveChangesAsync(ct);
    }

    private async Task AbrirContactoAsync(Seguimiento seg, CancellationToken ct)
    {
        seg.Estado = Contactando;
        seg.Intentos++;
        seg.UltimoIntentoUtc = AhoraUtc;
        await _db.SaveChangesAsync(ct);

        var (nombre, medico) = await NombresAsync(seg, ct);
        var r = await _notificador.NotificarPacienteAsync(seg.PacienteId,
            (idioma, consultorio) => TextosAgenda.SeguimientoContacto(idioma, consultorio, nombre, medico, seg.FechaObjetivo), "seguimiento", ct);
        seg.PorLlamada = !r.Enviado;
        await _db.SaveChangesAsync(ct);
        await _bitacora.RegistrarAsync(Bitacora.Sistema, "seguimiento_contacto", "seguimiento", Id(seg.Id), null,
            r.Enviado ? $"intento {seg.Intentos}" : "sin permiso: queda para llamar", ct);
    }

    /// <summary>Una vuelta del motor: reabre lo cancelado, abre contactos, reintenta, cierra sin respuesta, recuerda y repite.</summary>
    public async Task<ResultadoSeguimientos> ProcesarAsync(CancellationToken ct)
    {
        var ahora = AhoraUtc;
        int contactos = 0, reintentos = 0, sinRespuesta = 0, recordatorios = 0, recurrencias = 0, reabiertos = 0;

        // 1. Citas de seguimiento canceladas (desde el panel o por el paciente): el seguimiento vuelve a quedar abierto.
        var agendados = await _db.Seguimientos.Where(s => s.Estado == Agendado && s.CitaId != null).ToListAsync(ct);
        var idsCitas = agendados.Select(s => s.CitaId!.Value).ToList();
        var citas = await _db.Citas.AsNoTracking().Where(c => idsCitas.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        foreach (var s in agendados)
        {
            if (citas.TryGetValue(s.CitaId!.Value, out var c) && c.Estado == "cancelada")
            {
                Reabrir(s);
                reabiertos++;
            }
        }

        await _db.SaveChangesAsync(ct);

        // 2. Pendientes cuya fecha se acerca: se contacta al paciente para que escoja horario.
        var consultorio = await _db.Consultorios.AsNoTracking().Where(c => c.Id == _db.ConsultorioId)
            .Select(c => new { c.DiasContactoSeguimiento }).FirstAsync(ct);
        var limite = Hoy.AddDays(consultorio.DiasContactoSeguimiento);
        foreach (var s in await _db.Seguimientos.Where(s => s.Estado == Pendiente && s.FechaObjetivo <= limite).ToListAsync(ct))
        {
            await AbrirContactoAsync(s, ct);
            contactos++;
        }

        // 3. Sin respuesta: reintentos espaciados y, al agotarlos, "sin respuesta" para que la secretaria llame.
        var espera = TimeSpan.FromDays(Math.Max(1, _opciones.SeguimientoDiasEntreIntentos));
        foreach (var s in await _db.Seguimientos.Where(s => s.Estado == Contactando && !s.PorLlamada).ToListAsync(ct))
        {
            if (s.UltimoIntentoUtc is not { } ultimo || ahora - ultimo < espera)
                continue;
            if (s.Intentos >= _opciones.SeguimientoIntentos)
            {
                s.Estado = SinRespuesta;
                await _db.SaveChangesAsync(ct);
                await _bitacora.RegistrarAsync(Bitacora.Sistema, "seguimiento_sin_respuesta", "seguimiento", Id(s.Id), null, $"{s.Intentos} intentos", ct);
                sinRespuesta++;
            }
            else
            {
                await AbrirContactoAsync(s, ct);
                reintentos++;
            }
        }

        // 4. Recordatorio extra antes de la cita de seguimiento (el de 24 horas lo envía el motor de agenda).
        if (_opciones.SeguimientoRecordatorioDiasAntes > 0)
        {
            var dias = _opciones.SeguimientoRecordatorioDiasAntes;
            var hasta = ahora.AddDays(dias);
            var desde = ahora.AddHours(_opciones.RecordatorioHorasAntes + 24);
            var porRecordar = await _db.Citas.Include(c => c.Paciente).Include(c => c.Medico)
                .Where(c => c.SeguimientoId != null && c.Estado == "confirmada" && c.RecordatorioSieteDiasUtc == null &&
                            c.FechaHoraUtc > desde && c.FechaHoraUtc <= hasta && c.CreadaUtc <= c.FechaHoraUtc.AddDays(-dias))
                .ToListAsync(ct);
            foreach (var c in porRecordar)
            {
                c.RecordatorioSieteDiasUtc = ahora;
                await _db.SaveChangesAsync(ct);
                await _notificador.NotificarPacienteAsync(c.PacienteId, (idioma, cons) => TextosAgenda.RecordatorioSieteDias(
                    idioma, cons, c.Paciente!.Nombre, c.Medico!.Nombre, new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero), IdsDpm.Cita(c.Id)), "seguimiento", ct);
                recordatorios++;
            }
        }

        // 5. Recurrencia: cuando pasa la cita de un seguimiento recurrente, se crea el siguiente.
        var recurrentes = await _db.Seguimientos
            .Where(s => s.Estado == Agendado && s.RecurrenciaCada != null && !s.SiguienteCreado && s.CitaId != null).ToListAsync(ct);
        foreach (var s in recurrentes)
        {
            var cita = await _db.Citas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == s.CitaId, ct);
            if (cita is null || cita.Estado != "confirmada" || cita.FechaHoraUtc > ahora)
                continue;

            var fecha = HorarioConsultorio.FechaRd(new DateTimeOffset(cita.FechaHoraUtc, TimeSpan.Zero));
            var siguiente = s.RecurrenciaUnidad == "meses" ? fecha.AddMonths(s.RecurrenciaCada!.Value) : fecha.AddDays(7 * s.RecurrenciaCada!.Value);
            s.SiguienteCreado = true;
            await _db.SaveChangesAsync(ct);
            await CrearAsync(new NuevoSeguimiento(IdsDpm.Paciente(s.PacienteId), IdsDpm.Medico(s.MedicoId), siguiente, s.ToleranciaDias, s.Motivo,
                s.RecurrenciaCada, s.RecurrenciaUnidad), Bitacora.Sistema, ct);
            recurrencias++;
        }

        return new ResultadoSeguimientos(contactos, reintentos, sinRespuesta, recordatorios, recurrencias, reabiertos);
    }

    /// <summary>Vuelve a pendiente: si la fecha ya está cerca, la siguiente vuelta abre el contacto con el paciente.</summary>
    private void Reabrir(Seguimiento s)
    {
        s.Estado = Pendiente;
        s.CitaId = null;
        s.Intentos = 0;
        s.PorLlamada = false;
        s.UltimoIntentoUtc = null;
    }

    /// <summary>La cita de un seguimiento se canceló: queda abierto para volver a agendarlo.</summary>
    public async Task CitaCanceladaAsync(int citaId, CancellationToken ct)
    {
        var s = await _db.Seguimientos.FirstOrDefaultAsync(x => x.CitaId == citaId && x.Estado == Agendado, ct);
        if (s is null)
            return;
        Reabrir(s);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// El paciente escogió horario para su seguimiento: la cita debe caer dentro de la ventana que indicó el médico.
    /// Se valida antes de crear la cita.
    /// </summary>
    public async Task<Seguimiento> ValidarParaAgendarAsync(string seguimientoId, string pacienteId, DateTimeOffset fechaHora, string medicoId, CancellationToken ct)
    {
        var id = IdsDpm.Leer(seguimientoId, "SEG") ?? throw new ArgumentException($"No existe el seguimiento {seguimientoId}.");
        var paciente = IdsDpm.Leer(pacienteId, "PAC");
        var s = await _db.Seguimientos.FirstOrDefaultAsync(x => x.Id == id && x.PacienteId == paciente, ct)
                ?? throw new ArgumentException($"No encontré el seguimiento {seguimientoId} de este paciente.");
        if (s.Estado is not (Pendiente or Contactando or SinRespuesta))
            throw new InvalidOperationException($"El seguimiento {seguimientoId} ya está {s.Estado}.");
        if (IdsDpm.Leer(medicoId, "MED") != s.MedicoId)
            throw new ArgumentException($"El seguimiento es con {IdsDpm.Medico(s.MedicoId)}.");

        var fecha = HorarioConsultorio.FechaRd(fechaHora);
        if (fecha < s.FechaObjetivo.AddDays(-s.ToleranciaDias) || fecha > s.FechaObjetivo.AddDays(s.ToleranciaDias))
            throw new ArgumentException(
                $"El médico indicó el seguimiento entre el {s.FechaObjetivo.AddDays(-s.ToleranciaDias):yyyy-MM-dd} y el {s.FechaObjetivo.AddDays(s.ToleranciaDias):yyyy-MM-dd}. " +
                "Ofrece horarios dentro de esas fechas; si ninguno le sirve, usa escalar_a_personal.");
        return s;
    }

    public async Task VincularCitaAsync(Seguimiento s, string citaId, CancellationToken ct)
    {
        _db.Attach(s);
        await VincularAsync(s, IdsDpm.Leer(citaId, "CITA")!.Value, ct);
    }

    public async Task<bool> CancelarAsync(string seguimientoId, string actor, CancellationToken ct)
    {
        var s = IdsDpm.Leer(seguimientoId, "SEG") is { } id ? await _db.Seguimientos.FirstOrDefaultAsync(x => x.Id == id, ct) : null;
        if (s is null || s.Estado == Cancelado)
            return false;
        s.Estado = Cancelado;
        await _db.SaveChangesAsync(ct);
        await _bitacora.RegistrarAsync(actor, "seguimiento_cancelado", "seguimiento", Id(s.Id), null, null, ct);
        return true;
    }

    /// <summary>Vuelve a abrir el contacto de un seguimiento (por ejemplo, después de que la secretaria llamó).</summary>
    public async Task<bool> ReabrirAsync(string seguimientoId, string actor, CancellationToken ct)
    {
        var s = IdsDpm.Leer(seguimientoId, "SEG") is { } id ? await _db.Seguimientos.FirstOrDefaultAsync(x => x.Id == id, ct) : null;
        if (s is null || s.Estado is Agendado or Cancelado)
            return false;
        Reabrir(s);
        await _db.SaveChangesAsync(ct);
        await _bitacora.RegistrarAsync(actor, "seguimiento_reabierto", "seguimiento", Id(s.Id), null, null, ct);
        return true;
    }

    public async Task<IReadOnlyList<SeguimientoInfo>> ListarAsync(string? estado, CancellationToken ct, int? soloId = null, int? pacienteId = null)
    {
        var q = _db.Seguimientos.AsNoTracking().AsQueryable();
        if (soloId is { } sid)
            q = q.Where(s => s.Id == sid);
        if (pacienteId is { } pid)
            q = q.Where(s => s.PacienteId == pid);
        if (!string.IsNullOrWhiteSpace(estado))
            q = q.Where(s => s.Estado == estado);
        var lista = await q.OrderBy(s => s.FechaObjetivo).Take(500).ToListAsync(ct);

        var pacientes = await _db.Pacientes.AsNoTracking().Where(p => lista.Select(s => s.PacienteId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);
        var medicos = await _db.Medicos.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Nombre, ct);
        var idsCitas = lista.Where(s => s.CitaId != null).Select(s => s.CitaId!.Value).ToList();
        var citas = await _db.Citas.AsNoTracking().Where(c => idsCitas.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FechaHoraUtc, ct);

        return lista.Select(s => new SeguimientoInfo(
            Id(s.Id), IdsDpm.Paciente(s.PacienteId), pacientes.GetValueOrDefault(s.PacienteId, "?"), IdsDpm.Medico(s.MedicoId),
            medicos.GetValueOrDefault(s.MedicoId, "?"), s.FechaObjetivo, s.ToleranciaDias, s.FechaObjetivo.AddDays(-s.ToleranciaDias),
            s.FechaObjetivo.AddDays(s.ToleranciaDias), s.Motivo,
            s.RecurrenciaCada is { } c ? $"{c} {s.RecurrenciaUnidad}" : null, s.Estado, s.Intentos, s.PorLlamada,
            s.CitaId is { } ci ? IdsDpm.Cita(ci) : null,
            s.CitaId is { } cj && citas.TryGetValue(cj, out var f) ? new DateTimeOffset(f, TimeSpan.Zero) : null)).ToList();
    }

    private async Task<(string Paciente, string Medico)> NombresAsync(Seguimiento s, CancellationToken ct) =>
        (await _db.Pacientes.AsNoTracking().Where(p => p.Id == s.PacienteId).Select(p => p.Nombre).FirstAsync(ct),
         await _db.Medicos.AsNoTracking().Where(m => m.Id == s.MedicoId).Select(m => m.Nombre).FirstAsync(ct));

    public static string Id(int id) => $"SEG-{id:000}";
}
