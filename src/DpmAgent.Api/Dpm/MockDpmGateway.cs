using System.Collections.Concurrent;

namespace DpmAgent.Api.Dpm;

/// <summary>
/// DPM simulado en memoria, para pruebas y para correr sin base de datos. Todos los datos son ficticios.
/// </summary>
public sealed class MockDpmGateway : IDpmGateway
{
    public static readonly TimeZoneInfo ZonaRd = HorarioConsultorio.ZonaRd;

    private readonly ConcurrentDictionary<string, Paciente> _pacientes = new();
    private readonly ConcurrentDictionary<string, Cita> _citas = new();
    private readonly ConcurrentQueue<Escalamiento> _escalamientos = new();
    private readonly ConcurrentDictionary<string, CarnetLeido> _carnets = new();
    private readonly List<Medico> _medicos =
    [
        new("MED-01", "Dra. Ana Valdez (ficticia)", "Medicina General"),
        new("MED-02", "Dr. Luis Herrera (ficticio)", "Pediatría"),
    ];

    private readonly object _lock = new();
    private readonly TimeProvider _clock;
    private int _secPaciente = 100;
    private int _secCita = 1000;
    private int _secEscalamiento;
    private int _secCarnet;

    public MockDpmGateway(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;

        _pacientes["PAC-001"] = new("PAC-001", "María Rodríguez (ficticia)", "8095550101", "ARS Ejemplo");
        _pacientes["PAC-002"] = new("PAC-002", "José Martínez (ficticio)", "8295550102", null);
    }

    public Task<Paciente?> BuscarPacienteAsync(string telefonoOId, CancellationToken ct)
    {
        var telefono = Telefono.Normalizar(telefonoOId);
        var paciente = _pacientes.Values.FirstOrDefault(p =>
            p.Id.Equals(telefonoOId.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (telefono is not null && Telefono.Normalizar(p.Telefono) == telefono));

        return Task.FromResult(paciente);
    }

    public Task<Paciente> RegistrarPacienteAsync(string nombre, string telefono, string? ars, CancellationToken ct)
    {
        string id;
        lock (_lock) { id = $"PAC-{++_secPaciente:000}"; }

        var paciente = new Paciente(
            id,
            nombre.Trim(),
            Telefono.Normalizar(telefono) ?? throw new ArgumentException("El teléfono no es válido."),
            string.IsNullOrWhiteSpace(ars) ? null : ars.Trim());
        _pacientes[id] = paciente;
        return Task.FromResult(paciente);
    }

    public Task<IReadOnlyList<Medico>> ListarMedicosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Medico>>(_medicos);

    public Task<IReadOnlyList<DateTimeOffset>> ConsultarDisponibilidadAsync(string medicoId, DateOnly fecha, CancellationToken ct)
    {
        if (_medicos.All(m => m.Id != medicoId))
            throw new ArgumentException($"No existe el médico {medicoId}.");

        var ahora = _clock.GetUtcNow();
        var ocupadas = _citas.Values
            .Where(c => c.MedicoId == medicoId && c.Estado == "confirmada")
            .Select(c => c.FechaHora)
            .ToHashSet();

        IReadOnlyList<DateTimeOffset> libres = HorarioConsultorio.TurnosDelDia(fecha)
            .Where(t => t > ahora && !ocupadas.Contains(t))
            .ToList();

        return Task.FromResult(libres);
    }

    public async Task<Cita> CrearCitaAsync(string pacienteId, string medicoId, DateTimeOffset fechaHora, string motivo, CancellationToken ct)
    {
        if (!_pacientes.ContainsKey(pacienteId))
            throw new ArgumentException($"No existe el paciente {pacienteId}.");

        var disponibles = await ConsultarDisponibilidadAsync(medicoId, HorarioConsultorio.FechaRd(fechaHora), ct);
        if (!disponibles.Contains(fechaHora))
            throw new InvalidOperationException("Ese horario no está disponible.");

        lock (_lock)
        {
            // Segunda verificación dentro del lock para evitar doble reserva.
            if (_citas.Values.Any(c => c.MedicoId == medicoId && c.FechaHora == fechaHora && c.Estado == "confirmada"))
                throw new InvalidOperationException("Ese horario se acaba de ocupar.");

            var cita = new Cita($"CITA-{++_secCita}", pacienteId, medicoId, fechaHora, motivo.Trim(), "confirmada");
            _citas[cita.Id] = cita;
            return cita;
        }
    }

    public Task<IReadOnlyList<Cita>> CitasDePacienteAsync(string pacienteId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Cita>>(_citas.Values
            .Where(c => c.PacienteId == pacienteId && c.Estado == "confirmada")
            .OrderBy(c => c.FechaHora)
            .ToList());

    public Task<Cita?> CancelarCitaAsync(string citaId, string pacienteId, CancellationToken ct)
    {
        if (!_citas.TryGetValue(citaId.Trim().ToUpperInvariant(), out var cita) || cita.PacienteId != pacienteId)
            return Task.FromResult<Cita?>(null);

        var cancelada = cita with { Estado = "cancelada" };
        _citas[cita.Id] = cancelada;
        return Task.FromResult<Cita?>(cancelada);
    }

    public Task<Escalamiento> RegistrarEscalamientoAsync(NuevoEscalamiento nuevo, CancellationToken ct)
    {
        var e = new Escalamiento(
            $"ESC-{Interlocked.Increment(ref _secEscalamiento)}",
            nuevo.SesionId,
            nuevo.PacienteId,
            nuevo.ResumenEs.Trim(),
            nuevo.Urgente,
            "pendiente",
            _clock.GetUtcNow(),
            nuevo.ResumenEn?.Trim(),
            nuevo.MensajeOriginal,
            nuevo.Idioma);

        _escalamientos.Enqueue(e);
        return Task.FromResult(e);
    }

    /// <summary>Datos del consultorio simulado; las pruebas pueden cambiarlos.</summary>
    public InfoConsultorio Consultorio { get; set; } = new("Consultorio Ejemplo (ficticio)", "911", "es");

    public Task<InfoConsultorio> ObtenerConsultorioAsync(CancellationToken ct) => Task.FromResult(Consultorio);

    public Task<IReadOnlyList<Escalamiento>> ListarEscalamientosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Escalamiento>>(_escalamientos.Reverse().ToList());

    public Task<Paciente> ActualizarSeguroAsync(string pacienteId, string? ars, string? numeroAfiliado, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_pacientes.TryGetValue(pacienteId, out var p))
                throw new ArgumentException($"No existe el paciente {pacienteId}.");

            var actualizado = p with { Ars = ars ?? p.Ars, NumeroAfiliado = numeroAfiliado ?? p.NumeroAfiliado };
            _pacientes[pacienteId] = actualizado;
            return Task.FromResult(actualizado);
        }
    }

    public Task<CarnetLeido> GuardarCarnetPendienteAsync(
        string sesionId, string telefonoCanal, string? pacienteId, DatosCarnet datos, CancellationToken ct)
    {
        var carnet = new CarnetLeido(
            $"CAR-{Interlocked.Increment(ref _secCarnet)}",
            sesionId,
            telefonoCanal,
            pacienteId,
            datos,
            "pendiente",
            _clock.GetUtcNow());

        _carnets[carnet.Id] = carnet;
        return Task.FromResult(carnet);
    }

    public Task<CarnetLeido?> ObtenerCarnetAsync(string carnetId, CancellationToken ct) =>
        Task.FromResult(_carnets.GetValueOrDefault(carnetId.Trim().ToUpperInvariant()));

    public Task<CarnetLeido?> UltimoCarnetPendienteAsync(string sesionId, CancellationToken ct) =>
        Task.FromResult(_carnets.Values
            .Where(c => c.SesionId == sesionId && c.Estado == "pendiente")
            .MaxBy(c => IdsDpm.Leer(c.Id, "CAR")));

    public Task<IReadOnlyList<CarnetLeido>> ListarCarnetsPendientesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CarnetLeido>>(_carnets.Values
            .Where(c => c.Estado == "pendiente")
            .OrderBy(c => IdsDpm.Leer(c.Id, "CAR"))
            .ToList());

    public Task<CarnetLeido> ResolverCarnetAsync(
        string carnetId, string estado, DatosCarnet datos, string? pacienteId, string resueltoPor, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_carnets.TryGetValue(carnetId.Trim().ToUpperInvariant(), out var c))
                throw new ArgumentException($"No existe el carnet {carnetId}.");
            if (c.Estado != "pendiente")
                throw new InvalidOperationException($"El carnet {c.Id} ya fue {c.Estado}.");

            var resuelto = c with
            {
                Estado = estado,
                Datos = datos,
                PacienteId = pacienteId ?? c.PacienteId,
                ResueltoPor = resueltoPor,
                Resuelto = _clock.GetUtcNow(),
            };
            _carnets[c.Id] = resuelto;
            return Task.FromResult(resuelto);
        }
    }

    public static DateTimeOffset EnHoraRd(DateOnly fecha, TimeOnly hora) => HorarioConsultorio.EnHoraRd(fecha, hora);
}
