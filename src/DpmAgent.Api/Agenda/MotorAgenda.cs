using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Agenda;

public sealed record ResultadoMotor(int OfertasVencidas, int OfertasEnviadas, int HuecosCerrados, int Recordatorios);

/// <summary>
/// Trabajo automático de un consultorio, en cada vuelta:
/// 1. vence las ofertas que nadie respondió a tiempo;
/// 2. ofrece cada hueco liberado al siguiente paciente de la lista de espera (uno a la vez);
/// 3. envía el recordatorio de 24 horas.
/// Es idempotente: si se cae a mitad, la siguiente vuelta sigue donde quedó.
/// </summary>
public sealed class MotorAgenda
{
    private readonly DpmDbContext _db;
    private readonly Notificador _notificador;
    private readonly Bitacora _bitacora;
    private readonly AgendaOptions _opciones;
    private readonly TimeProvider _clock;
    private readonly ILogger<MotorAgenda> _logger;

    public MotorAgenda(
        DpmDbContext db,
        Notificador notificador,
        Bitacora bitacora,
        IOptions<AgendaOptions> opciones,
        TimeProvider clock,
        ILogger<MotorAgenda> logger)
    {
        _db = db;
        _notificador = notificador;
        _bitacora = bitacora;
        _opciones = opciones.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ResultadoMotor> ProcesarAsync(CancellationToken ct)
    {
        var vencidas = await VencerOfertasAsync(ct);
        var (enviadas, cerrados) = await LlenarHuecosAsync(ct);
        var recordatorios = await EnviarRecordatoriosAsync(ct);
        return new ResultadoMotor(vencidas, enviadas, cerrados, recordatorios);
    }

    private async Task<int> VencerOfertasAsync(CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var vencidas = await _db.OfertasHueco.Where(o => o.Estado == "pendiente" && o.VenceUtc <= ahora).ToListAsync(ct);

        var total = 0;
        foreach (var o in vencidas)
        {
            o.Estado = "vencida";
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // El paciente respondió justo ahora: gana su respuesta.
                _db.Entry(o).State = EntityState.Detached;
                continue;
            }

            total++;
            await _bitacora.RegistrarAsync(Bitacora.Sistema, "oferta_vencida", "oferta", $"OF-{o.Id}", null,
                $"{IdsDpm.Paciente(o.PacienteId)} no respondió a tiempo", ct);
            await _notificador.NotificarPacienteAsync(o.PacienteId,
                TextosAgenda.OfertaVencida(new DateTimeOffset(o.FechaHoraUtc, TimeSpan.Zero)), "aviso", ct);
        }

        return total;
    }

    private async Task<(int Enviadas, int Cerrados)> LlenarHuecosAsync(CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var limite = ahora.AddMinutes(_opciones.AnticipacionMinimaMinutos);

        var canceladas = await _db.Citas
            .Where(c => c.Estado == "cancelada" && c.HuecoProcesadoUtc == null)
            .ToListAsync(ct);

        var enviadas = 0;
        var cerrados = 0;
        foreach (var hueco in canceladas.GroupBy(c => (c.MedicoId, c.FechaHoraUtc)))
        {
            var (medicoId, fechaUtc) = hueco.Key;

            if (fechaUtc <= limite || await HuecoOcupadoAsync(medicoId, fechaUtc, ct))
            {
                await CerrarHuecoAsync(hueco, ahora, ct);
                cerrados++;
                continue;
            }

            // Una oferta a la vez por hueco: si hay una pendiente (o una aceptación en curso), se espera.
            var ofertas = await _db.OfertasHueco.AsNoTracking()
                .Where(o => o.MedicoId == medicoId && o.FechaHoraUtc == fechaUtc)
                .ToListAsync(ct);
            if (ofertas.Any(o => o.Estado is "pendiente" or "aceptada"))
                continue;

            var candidato = await SiguienteCandidatoAsync(
                medicoId, fechaUtc, ofertas.Select(o => o.ListaEsperaId).ToHashSet(), hueco.Select(c => c.PacienteId).ToHashSet(), ct);

            if (candidato is null)
            {
                await CerrarHuecoAsync(hueco, ahora, ct);
                cerrados++;
                continue;
            }

            await OfrecerAsync(candidato, fechaUtc, ahora, ct);
            enviadas++;
        }

        return (enviadas, cerrados);
    }

    private async Task<ListaEspera?> SiguienteCandidatoAsync(
        int medicoId, DateTime fechaUtc, HashSet<int> yaOfrecidas, HashSet<int> quienesCancelaron, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(new DateTimeOffset(fechaUtc, TimeSpan.Zero), HorarioConsultorio.ZonaRd);
        var fecha = DateOnly.FromDateTime(local.DateTime);
        var hora = TimeOnly.FromDateTime(local.DateTime);

        var activas = await _db.ListaEspera.Where(l => l.MedicoId == medicoId && l.Estado == "activa").OrderBy(l => l.CreadaUtc).ThenBy(l => l.Id).ToListAsync(ct);
        var conOfertaPendiente = await _db.OfertasHueco.AsNoTracking()
            .Where(o => o.Estado == "pendiente")
            .Select(o => o.ListaEsperaId)
            .ToListAsync(ct);

        return activas.FirstOrDefault(l =>
            l.Desde <= fecha && fecha <= l.Hasta &&
            l.HoraDesde <= hora && hora < l.HoraHasta &&
            !yaOfrecidas.Contains(l.Id) &&
            !conOfertaPendiente.Contains(l.Id) &&
            !quienesCancelaron.Contains(l.PacienteId));
    }

    private async Task OfrecerAsync(ListaEspera entrada, DateTime fechaUtc, DateTime ahora, CancellationToken ct)
    {
        var vence = new[] { ahora.AddMinutes(_opciones.OfertaMinutos), fechaUtc.AddMinutes(-_opciones.AnticipacionMinimaMinutos) }.Min();
        var oferta = new OfertaHueco
        {
            ConsultorioId = _db.ConsultorioId,
            ListaEsperaId = entrada.Id,
            PacienteId = entrada.PacienteId,
            MedicoId = entrada.MedicoId,
            FechaHoraUtc = fechaUtc,
            Estado = "pendiente",
            CreadaUtc = ahora,
            VenceUtc = vence,
        };
        _db.OfertasHueco.Add(oferta);
        await _db.SaveChangesAsync(ct);

        var paciente = await _db.Pacientes.AsNoTracking().FirstAsync(p => p.Id == entrada.PacienteId, ct);
        var medico = await _db.Medicos.AsNoTracking().FirstAsync(m => m.Id == entrada.MedicoId, ct);
        await _notificador.NotificarPacienteAsync(entrada.PacienteId, TextosAgenda.Oferta(
            paciente.Nombre, medico.Nombre, new DateTimeOffset(fechaUtc, TimeSpan.Zero), new DateTimeOffset(vence, TimeSpan.Zero)), "oferta", ct);

        await _bitacora.RegistrarAsync(Bitacora.Sistema, "oferta_enviada", "oferta", $"OF-{oferta.Id}", null,
            $"{IdsDpm.Paciente(entrada.PacienteId)} para {IdsDpm.Medico(entrada.MedicoId)} {fechaUtc:yyyy-MM-dd HH:mm} UTC", ct);
        _logger.LogInformation("Oferta OF-{Id} enviada a {Paciente}", oferta.Id, IdsDpm.Paciente(entrada.PacienteId));
    }

    private Task<bool> HuecoOcupadoAsync(int medicoId, DateTime fechaUtc, CancellationToken ct) =>
        _db.Citas.AnyAsync(c => c.MedicoId == medicoId && c.FechaHoraUtc == fechaUtc && c.Estado == "confirmada", ct);

    private async Task CerrarHuecoAsync(IEnumerable<Data.Entidades.Cita> citas, DateTime ahora, CancellationToken ct)
    {
        foreach (var c in citas)
            c.HuecoProcesadoUtc = ahora;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> EnviarRecordatoriosAsync(CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var hasta = ahora.AddHours(_opciones.RecordatorioHorasAntes);
        var desde = ahora.AddHours(_opciones.RecordatorioMinimoHoras);
        var creadaAntesDe = ahora.AddHours(-_opciones.RecordatorioSiCreadaHaceHoras);

        var citas = await _db.Citas
            .Include(c => c.Paciente)
            .Include(c => c.Medico)
            .Where(c => c.Estado == "confirmada" && c.RecordatorioEnviadoUtc == null &&
                        c.FechaHoraUtc >= desde && c.FechaHoraUtc <= hasta && c.CreadaUtc <= creadaAntesDe)
            .ToListAsync(ct);

        foreach (var c in citas)
        {
            // Se marca antes de enviar: es preferible perder un recordatorio que mandarlo dos veces.
            c.RecordatorioEnviadoUtc = ahora;
            await _db.SaveChangesAsync(ct);

            var texto = TextosAgenda.Recordatorio(c.Paciente!.Nombre, c.Medico!.Nombre, new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero))
                        + $" (cita {IdsDpm.Cita(c.Id)})";
            await _notificador.NotificarPacienteAsync(c.PacienteId, texto, "recordatorio", ct);
            await _bitacora.RegistrarAsync(Bitacora.Sistema, "recordatorio_enviado", "cita", IdsDpm.Cita(c.Id), null, null, ct);
        }

        return citas.Count;
    }
}
