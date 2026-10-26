using System.Collections.Concurrent;

namespace DpmAgent.Api.Dpm;

/// <summary>
/// DPM simulado en memoria. Todos los datos son ficticios.
/// Horario: lunes a viernes, 8:00 a 12:00 y 14:00 a 17:00, citas de 30 minutos.
/// </summary>
public sealed class MockDpmGateway : IDpmGateway
{
    public static readonly TimeZoneInfo ZonaRd = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private readonly ConcurrentDictionary<string, Paciente> _pacientes = new();
    private readonly ConcurrentDictionary<string, Cita> _citas = new();
    private readonly List<Medico> _medicos =
    [
        new("MED-01", "Dra. Ana Valdez (ficticia)", "Medicina General"),
        new("MED-02", "Dr. Luis Herrera (ficticio)", "Pediatría"),
    ];

    private readonly object _lock = new();
    private readonly TimeProvider _clock;
    private int _secPaciente = 100;
    private int _secCita = 1000;

    public MockDpmGateway(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;

        _pacientes["PAC-001"] = new("PAC-001", "María Rodríguez (ficticia)", "8095550101", "ARS Ejemplo");
        _pacientes["PAC-002"] = new("PAC-002", "José Martínez (ficticio)", "8295550102", null);
    }

    public Task<Paciente?> BuscarPacienteAsync(string telefonoOId, CancellationToken ct)
    {
        var clave = SoloDigitos(telefonoOId);
        var paciente = _pacientes.Values.FirstOrDefault(p =>
            p.Id.Equals(telefonoOId.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (clave.Length >= 7 && SoloDigitos(p.Telefono).EndsWith(clave[^Math.Min(10, clave.Length)..])));

        return Task.FromResult(paciente);
    }

    public Task<Paciente> RegistrarPacienteAsync(string nombre, string telefono, string? ars, CancellationToken ct)
    {
        string id;
        lock (_lock) { id = $"PAC-{++_secPaciente:000}"; }

        var paciente = new Paciente(id, nombre.Trim(), SoloDigitos(telefono), string.IsNullOrWhiteSpace(ars) ? null : ars.Trim());
        _pacientes[id] = paciente;
        return Task.FromResult(paciente);
    }

    public Task<IReadOnlyList<Medico>> ListarMedicosAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Medico>>(_medicos);

    public Task<IReadOnlyList<DateTimeOffset>> ConsultarDisponibilidadAsync(string medicoId, DateOnly fecha, CancellationToken ct)
    {
        if (_medicos.All(m => m.Id != medicoId))
            throw new ArgumentException($"No existe el médico {medicoId}.");

        var libres = new List<DateTimeOffset>();
        if (fecha.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return Task.FromResult<IReadOnlyList<DateTimeOffset>>(libres);

        var ahora = _clock.GetUtcNow();
        var ocupadas = _citas.Values
            .Where(c => c.MedicoId == medicoId && c.Estado == "confirmada")
            .Select(c => c.FechaHora)
            .ToHashSet();

        foreach (var (inicio, fin) in new[] { (8, 12), (14, 17) })
        {
            for (var t = new TimeOnly(inicio, 0); t < new TimeOnly(fin, 0); t = t.AddMinutes(30))
            {
                var slot = EnHoraRd(fecha, t);
                if (slot > ahora && !ocupadas.Contains(slot))
                    libres.Add(slot);
            }
        }

        return Task.FromResult<IReadOnlyList<DateTimeOffset>>(libres);
    }

    public async Task<Cita> CrearCitaAsync(string pacienteId, string medicoId, DateTimeOffset fechaHora, string motivo, CancellationToken ct)
    {
        if (!_pacientes.ContainsKey(pacienteId))
            throw new ArgumentException($"No existe el paciente {pacienteId}.");

        var local = TimeZoneInfo.ConvertTime(fechaHora, ZonaRd);
        var disponibles = await ConsultarDisponibilidadAsync(medicoId, DateOnly.FromDateTime(local.DateTime), ct);
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
        _citas[citaId] = cancelada;
        return Task.FromResult<Cita?>(cancelada);
    }

    public static DateTimeOffset EnHoraRd(DateOnly fecha, TimeOnly hora)
    {
        var local = fecha.ToDateTime(hora, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, ZonaRd.GetUtcOffset(local));
    }

    private static string SoloDigitos(string s) => new(s.Where(char.IsDigit).ToArray());
}
