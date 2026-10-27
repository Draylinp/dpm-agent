using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

public sealed record CitaParaReprogramar(
    string Id, string PacienteId, string Paciente, string MedicoId, string Medico, DateTimeOffset FechaHora, string Estado, bool Prioridad,
    string? NuevaCitaId);

public sealed record ResultadoReprogramacion(int Contactados, int ParaLlamar, int SinRespuesta);

/// <summary>
/// Cambios de agenda que vienen del consultorio: el médico bloquea días (reprogramación masiva), llega tarde
/// (aviso de retraso) o un paciente no asistió (se le ofrece reagendar una vez). Nada de esto evalúa la salud del paciente.
/// </summary>
public sealed class ServicioReprogramacion(DpmDbContext db, IDpmGateway dpm, Notificador notificador, Bitacora bitacora, TimeProvider clock)
{
    public const string Pendiente = "pendiente";
    public const string Contactado = "contactado";
    public const string Reprogramada = "reprogramada";
    public const string Llamar = "llamar";
    public const string SinRespuesta = "sin_respuesta";

    /// <summary>Días hacia adelante en que se buscan horarios nuevos.</summary>
    private const int DiasBusqueda = 21;

    /// <summary>Sin respuesta pasado este tiempo, la cita queda para que la secretaria llame.</summary>
    private static readonly TimeSpan EsperaRespuesta = TimeSpan.FromHours(48);

    private DateTimeOffset Ahora => clock.GetUtcNow();

    /// <summary>
    /// Contacta a cada paciente con cita en días bloqueados, con hasta 3 horarios del mismo médico. Primero las citas de
    /// seguimientos y las urgentes, que se quedan con los primeros horarios; no se le ofrece el mismo horario a dos pacientes.
    /// </summary>
    public async Task<ResultadoReprogramacion> ProcesarAsync(CancellationToken ct)
    {
        int contactados = 0, llamar = 0, sinRespuesta = 0;
        var limite = Ahora.UtcDateTime - EsperaRespuesta;
        foreach (var c in await db.Citas.Where(c => c.Reprogramacion == Contactado && c.Estado == "confirmada" && c.ReprogramacionContactoUtc < limite).ToListAsync(ct))
        {
            c.Reprogramacion = SinRespuesta;
            sinRespuesta++;
        }

        await db.SaveChangesAsync(ct);

        var pendientes = await db.Citas.Include(c => c.Paciente).Include(c => c.Medico)
            .Where(c => c.Reprogramacion == Pendiente && c.Estado == "confirmada")
            .OrderByDescending(c => c.SeguimientoId != null || c.Urgente).ThenBy(c => c.FechaHoraUtc)
            .ToListAsync(ct);
        var ofrecidos = new HashSet<(int, DateTimeOffset)>();
        foreach (var c in pendientes)
        {
            var opciones = new List<DateTimeOffset>();
            var desde = HorarioConsultorio.FechaRd(Ahora);
            for (var dia = desde; dia <= desde.AddDays(DiasBusqueda) && opciones.Count < 3; dia = dia.AddDays(1))
            {
                foreach (var t in await dpm.ConsultarDisponibilidadAsync(IdsDpm.Medico(c.MedicoId), dia, ct))
                {
                    if (opciones.Count < 3 && ofrecidos.Add((c.MedicoId, t)))
                        opciones.Add(t);
                }
            }

            var fecha = new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero);
            var r = opciones.Count == 0
                ? null
                : await notificador.NotificarPacienteAsync(c.PacienteId, (idioma, cons) =>
                    TextosAgenda.Reprogramar(idioma, cons, c.Paciente!.Nombre, c.Medico!.Nombre, fecha, IdsDpm.Cita(c.Id), opciones), "reprogramar", ct);

            if (r is { Enviado: true })
            {
                c.Reprogramacion = Contactado;
                contactados++;
            }
            else
            {
                // Sin permiso de avisos o sin horarios: la secretaria llama.
                c.Reprogramacion = Llamar;
                llamar++;
            }

            c.ReprogramacionContactoUtc = Ahora.UtcDateTime;
            await db.SaveChangesAsync(ct);
            await bitacora.RegistrarAsync(Bitacora.Sistema, "reprogramacion_" + c.Reprogramacion, "cita", IdsDpm.Cita(c.Id), null, $"{opciones.Count} opciones", ct);
        }

        return new ResultadoReprogramacion(contactados, llamar, sinRespuesta);
    }

    /// <summary>El paciente escoge un horario nuevo para su cita afectada: se crea la nueva y se cancela la anterior.</summary>
    public async Task<Cita> ReprogramarAsync(string pacienteId, string citaId, string? medicoId, DateTimeOffset fechaHora, CancellationToken ct)
    {
        var pid = IdsDpm.Leer(pacienteId, "PAC");
        // La cita de otro paciente se ve igual que una que no existe.
        var vieja = await db.Citas.FirstOrDefaultAsync(c => c.Id == IdsDpm.Leer(citaId, "CITA") && c.PacienteId == pid && c.Estado == "confirmada", ct)
                    ?? throw new ArgumentException($"No existe la cita {citaId}.");
        if (!vieja.ParaReprogramar)
            throw new InvalidOperationException("Esa cita no está para reprogramar. Para cambiarla, cancélala (en dos pasos) y crea otra.");

        var nueva = await dpm.CrearCitaAsync(pacienteId, medicoId ?? IdsDpm.Medico(vieja.MedicoId), fechaHora, vieja.Motivo, ct);
        var nuevaDb = await db.Citas.FirstAsync(c => c.Id == IdsDpm.Leer(nueva.Id, "CITA"), ct);
        nuevaDb.SeguimientoId = vieja.SeguimientoId;
        nuevaDb.Urgente = vieja.Urgente;
        vieja.Estado = "cancelada";
        vieja.CanceladaUtc = Ahora.UtcDateTime;
        vieja.HuecoProcesadoUtc = Ahora.UtcDateTime; // el día está bloqueado: no hay hueco que ofrecer
        vieja.Reprogramacion = Reprogramada;
        vieja.ReprogramadaAId = nuevaDb.Id;
        if (vieja.SeguimientoId is { } sid && await db.Seguimientos.FirstOrDefaultAsync(s => s.Id == sid, ct) is { } seg)
            seg.CitaId = nuevaDb.Id;
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(Bitacora.Agente, "cita_reprogramada", "cita", citaId, null, nueva.Id, ct);
        return nueva;
    }

    /// <summary>Ids de las citas confirmadas del paciente que hay que mover.</summary>
    public async Task<HashSet<string>> ParaReprogramarAsync(string pacienteId, CancellationToken ct)
    {
        var pid = IdsDpm.Leer(pacienteId, "PAC");
        var ids = await db.Citas.AsNoTracking().Where(c => c.PacienteId == pid && c.Estado == "confirmada" && c.ParaReprogramar).Select(c => c.Id).ToListAsync(ct);
        return ids.Select(IdsDpm.Cita).ToHashSet();
    }

    public async Task<IReadOnlyList<CitaParaReprogramar>> ListarAsync(CancellationToken ct)
    {
        var lista = await db.Citas.AsNoTracking().Include(c => c.Paciente).Include(c => c.Medico)
            .Where(c => c.Reprogramacion != null).OrderByDescending(c => c.Id).Take(200).ToListAsync(ct);
        return lista.Select(c => new CitaParaReprogramar(IdsDpm.Cita(c.Id), IdsDpm.Paciente(c.PacienteId), c.Paciente!.Nombre, IdsDpm.Medico(c.MedicoId),
            c.Medico!.Nombre, new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero), c.Reprogramacion!, c.SeguimientoId is not null || c.Urgente,
            c.ReprogramadaAId is { } n ? IdsDpm.Cita(n) : null)).ToList();
    }

    /// <summary>El médico va con retraso: aviso a los pacientes que le quedan hoy (citas confirmadas que todavía no empiezan).</summary>
    public async Task<int> AvisarRetrasoAsync(string medicoId, int minutos, string actor, CancellationToken ct)
    {
        if (minutos is < 5 or > 240)
            throw new ArgumentException("El retraso debe estar entre 5 y 240 minutos.");
        var medico = IdsDpm.Leer(medicoId, "MED") is { } m && await db.Medicos.FirstOrDefaultAsync(x => x.Id == m, ct) is { } med
            ? med : throw new ArgumentException($"No existe el médico {medicoId}.");
        var hoy = HorarioConsultorio.FechaRd(Ahora);
        var fin = HorarioConsultorio.EnHoraRd(hoy.AddDays(1), TimeOnly.MinValue).UtcDateTime;
        var citas = await db.Citas.AsNoTracking().Include(c => c.Paciente)
            .Where(c => c.MedicoId == medico.Id && c.Estado == "confirmada" && c.FechaHoraUtc > Ahora.UtcDateTime && c.FechaHoraUtc < fin)
            .OrderBy(c => c.FechaHoraUtc).ToListAsync(ct);

        foreach (var c in citas)
        {
            var fecha = new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero);
            await notificador.NotificarPacienteAsync(c.PacienteId, (idioma, cons) =>
                TextosAgenda.Retraso(idioma, cons, c.Paciente!.Nombre, medico.Nombre, minutos, fecha), "retraso", ct);
        }

        await bitacora.RegistrarAsync(actor, "retraso_avisado", "medico", IdsDpm.Medico(medico.Id), null, $"{minutos} min; {citas.Count} pacientes", ct);
        return citas.Count;
    }

    /// <summary>
    /// La secretaria marca que el paciente no vino (solo citas que ya pasaron). Se le ofrece reagendar una sola vez;
    /// el contador de ausencias es la cantidad de citas "no_asistio".
    /// </summary>
    public async Task<int> MarcarAusenciaAsync(string citaId, string actor, CancellationToken ct)
    {
        var c = await db.Citas.Include(x => x.Paciente).Include(x => x.Medico).FirstOrDefaultAsync(x => x.Id == IdsDpm.Leer(citaId, "CITA"), ct)
                ?? throw new ArgumentException($"No existe la cita {citaId}.");
        if (c.Estado != "confirmada")
            throw new InvalidOperationException($"La cita está {c.Estado}.");
        if (c.FechaHoraUtc > Ahora.UtcDateTime)
            throw new InvalidOperationException("La cita todavía no ha pasado.");

        c.Estado = "no_asistio";
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, "no_asistio", "cita", citaId, null, null, ct);

        if (c.AusenciaOfrecidaUtc is null)
        {
            var fecha = new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero);
            await notificador.NotificarPacienteAsync(c.PacienteId, (idioma, cons) =>
                TextosAgenda.Ausencia(idioma, cons, c.Paciente!.Nombre, c.Medico!.Nombre, fecha), "ausencia", ct);
            c.AusenciaOfrecidaUtc = Ahora.UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        return await db.Citas.CountAsync(x => x.PacienteId == c.PacienteId && x.Estado == "no_asistio", ct);
    }

    public static Task<Dictionary<int, int>> AusenciasAsync(DpmDbContext db, IEnumerable<int> pacientes, CancellationToken ct)
    {
        var ids = pacientes.Distinct().ToList();
        return db.Citas.AsNoTracking().Where(c => c.Estado == "no_asistio" && ids.Contains(c.PacienteId))
            .GroupBy(c => c.PacienteId).ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }

}
