using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

public sealed record EntradaListaEspera(
    string Id, string PacienteId, string MedicoId, DateOnly Desde, DateOnly Hasta, TimeOnly HoraDesde, TimeOnly HoraHasta, string Estado);

public sealed record RespuestaOferta(bool Aceptada, Dpm.Cita? Cita, DateTimeOffset FechaHora, string MedicoId);

/// <summary>
/// Lista de espera y respuestas del paciente a mensajes automáticos (ofertas y recordatorios).
/// Todas las operaciones reciben el paciente de la sesión: nunca actúan sobre otro.
/// </summary>
public sealed class ServicioListaEspera
{
    private const int MaxDiasRango = 60;
    private static readonly TimeOnly PrimeraHora = new(7, 0);
    private static readonly TimeOnly UltimaHora = new(18, 0);

    private readonly DpmDbContext _db;
    private readonly IDpmGateway _dpm;
    private readonly DespertadorAgenda _despertador;
    private readonly TimeProvider _clock;

    public ServicioListaEspera(DpmDbContext db, IDpmGateway dpm, DespertadorAgenda despertador, TimeProvider clock)
    {
        _db = db;
        _dpm = dpm;
        _despertador = despertador;
        _clock = clock;
    }

    private DateTime AhoraUtc => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Agrega al paciente a la lista de espera del médico. Si ya estaba, actualiza sus preferencias.</summary>
    public async Task<EntradaListaEspera> AgregarAsync(
        string pacienteId, string medicoId, DateOnly desde, DateOnly hasta, TimeOnly horaDesde, TimeOnly horaHasta, CancellationToken ct)
    {
        var paciente = IdsDpm.Leer(pacienteId, "PAC") ?? throw new ArgumentException($"No existe el paciente {pacienteId}.");
        var medico = IdsDpm.Leer(medicoId, "MED") is { } m && await _db.Medicos.AnyAsync(x => x.Id == m && x.Activo, ct)
            ? m
            : throw new ArgumentException($"No existe el médico {medicoId}.");

        var hoy = HorarioConsultorio.FechaRd(_clock.GetUtcNow());
        if (desde < hoy)
            throw new ArgumentException("La fecha 'desde' no puede ser anterior a hoy.");
        if (hasta < desde)
            throw new ArgumentException("La fecha 'hasta' debe ser igual o posterior a 'desde'.");
        if (hasta.DayNumber - desde.DayNumber > MaxDiasRango)
            throw new ArgumentException($"El rango no puede pasar de {MaxDiasRango} días.");
        if (horaDesde >= horaHasta || horaDesde < PrimeraHora || horaHasta > UltimaHora)
            throw new ArgumentException("El rango de horas debe estar entre 07:00 y 18:00, con 'hora_desde' antes de 'hora_hasta'.");

        var entrada = await _db.ListaEspera
            .FirstOrDefaultAsync(l => l.PacienteId == paciente && l.MedicoId == medico && l.Estado == "activa", ct);

        if (entrada is null)
        {
            entrada = new ListaEspera
            {
                ConsultorioId = _db.ConsultorioId,
                PacienteId = paciente,
                MedicoId = medico,
                Estado = "activa",
                CreadaUtc = AhoraUtc,
            };
            _db.ListaEspera.Add(entrada);
        }

        entrada.Desde = desde;
        entrada.Hasta = hasta;
        entrada.HoraDesde = horaDesde;
        entrada.HoraHasta = horaHasta;
        await _db.SaveChangesAsync(ct);

        // Puede haber huecos ya liberados que le sirvan.
        _despertador.Despertar();
        return AEntrada(entrada);
    }

    public async Task<IReadOnlyList<EntradaListaEspera>> ListarActivasAsync(CancellationToken ct)
    {
        var lista = await _db.ListaEspera.AsNoTracking().Where(l => l.Estado == "activa").OrderBy(l => l.Id).ToListAsync(ct);
        return lista.Select(AEntrada).ToList();
    }

    /// <summary>
    /// Respuesta del paciente a la oferta pendiente más reciente. Si acepta, se crea la cita
    /// (validando de nuevo que el hueco siga libre); si rechaza, el motor pasa al siguiente candidato.
    /// </summary>
    public async Task<RespuestaOferta> ResponderOfertaAsync(string pacienteId, bool acepta, CancellationToken ct)
    {
        var paciente = IdsDpm.Leer(pacienteId, "PAC") ?? throw new ArgumentException($"No existe el paciente {pacienteId}.");
        var ahora = AhoraUtc;

        var oferta = await _db.OfertasHueco
            .Where(o => o.PacienteId == paciente && o.Estado == "pendiente")
            .OrderByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No hay ninguna oferta de turno pendiente para este paciente.");

        if (oferta.VenceUtc <= ahora)
            throw new InvalidOperationException("La oferta ya venció. El paciente sigue en la lista de espera.");

        var fechaHora = new DateTimeOffset(oferta.FechaHoraUtc, TimeSpan.Zero);
        var medicoId = IdsDpm.Medico(oferta.MedicoId);
        oferta.RespondidaUtc = ahora;

        if (!acepta)
        {
            oferta.Estado = "rechazada";
            await GuardarOfertaAsync(ct);
            _despertador.Despertar();
            return new RespuestaOferta(false, null, fechaHora, medicoId);
        }

        // Primero se reclama la oferta: si el motor la venció en este instante, falla aquí y no se crea nada.
        oferta.Estado = "aceptada";
        await GuardarOfertaAsync(ct);

        Dpm.Cita cita;
        try
        {
            cita = await _dpm.CrearCitaAsync(IdsDpm.Paciente(paciente), medicoId, fechaHora, "lista de espera", ct);
        }
        catch (InvalidOperationException)
        {
            oferta.Estado = "invalida";
            await _db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Ese turno ya no está disponible.");
        }

        oferta.CitaId = IdsDpm.Leer(cita.Id, "CITA");

        var entrada = await _db.ListaEspera.FirstAsync(l => l.Id == oferta.ListaEsperaId, ct);
        entrada.Estado = "atendida";

        var canceladas = await _db.Citas
            .Where(c => c.MedicoId == oferta.MedicoId && c.FechaHoraUtc == oferta.FechaHoraUtc && c.Estado == "cancelada" && c.HuecoProcesadoUtc == null)
            .ToListAsync(ct);
        foreach (var c in canceladas)
            c.HuecoProcesadoUtc = ahora;

        await GuardarOfertaAsync(ct);
        return new RespuestaOferta(true, cita, fechaHora, medicoId);
    }

    /// <summary>El paciente confirma que asistirá a una cita suya futura (respuesta al recordatorio).</summary>
    public async Task<Dpm.Cita> ConfirmarAsistenciaAsync(string pacienteId, string citaId, CancellationToken ct)
    {
        var paciente = IdsDpm.Leer(pacienteId, "PAC");
        var id = IdsDpm.Leer(citaId, "CITA");
        var ahora = AhoraUtc;

        var cita = await _db.Citas.FirstOrDefaultAsync(
                       c => c.Id == id && c.PacienteId == paciente && c.Estado == "confirmada" && c.FechaHoraUtc > ahora, ct)
                   ?? throw new InvalidOperationException($"No encontré la cita {citaId} entre las próximas citas de este paciente.");

        cita.AsistenciaConfirmadaUtc ??= ahora;
        await _db.SaveChangesAsync(ct);

        return (await _dpm.CitasDePacienteAsync(IdsDpm.Paciente(cita.PacienteId), ct)).First(c => c.Id == citaId.Trim().ToUpperInvariant());
    }

    /// <summary>Avisa al motor que se liberó un hueco, para que no espere al siguiente intervalo.</summary>
    public void AvisarHuecoLiberado() => _despertador.Despertar();

    private async Task GuardarOfertaAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("La oferta cambió mientras se respondía (pudo vencer). Intente de nuevo.");
        }
    }

    private static EntradaListaEspera AEntrada(ListaEspera l) => new(
        $"LE-{l.Id}", IdsDpm.Paciente(l.PacienteId), IdsDpm.Medico(l.MedicoId), l.Desde, l.Hasta, l.HoraDesde, l.HoraHasta, l.Estado);
}
