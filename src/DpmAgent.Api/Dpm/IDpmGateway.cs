namespace DpmAgent.Api.Dpm;

public sealed record Paciente(string Id, string Nombre, string Telefono, string? Ars);

public sealed record Medico(string Id, string Nombre, string Especialidad);

public sealed record Cita(
    string Id,
    string PacienteId,
    string MedicoId,
    DateTimeOffset FechaHora,
    string Motivo,
    string Estado);

/// <summary>
/// Puerta de entrada al sistema DPM. Durante el hackathon se usa una implementación
/// simulada con datos ficticios; luego se puede conectar a la API real de DPM.
/// </summary>
public interface IDpmGateway
{
    Task<Paciente?> BuscarPacienteAsync(string telefonoOId, CancellationToken ct);

    Task<Paciente> RegistrarPacienteAsync(string nombre, string telefono, string? ars, CancellationToken ct);

    Task<IReadOnlyList<Medico>> ListarMedicosAsync(CancellationToken ct);

    Task<IReadOnlyList<DateTimeOffset>> ConsultarDisponibilidadAsync(string medicoId, DateOnly fecha, CancellationToken ct);

    Task<Cita> CrearCitaAsync(string pacienteId, string medicoId, DateTimeOffset fechaHora, string motivo, CancellationToken ct);

    Task<IReadOnlyList<Cita>> CitasDePacienteAsync(string pacienteId, CancellationToken ct);

    Task<Cita?> CancelarCitaAsync(string citaId, CancellationToken ct);
}
