namespace DpmAgent.Api.Dpm;

public sealed record Paciente(string Id, string Nombre, string Telefono, string? Ars, string? NumeroAfiliado = null);

public sealed record Medico(string Id, string Nombre, string Especialidad);

public sealed record Cita(
    string Id,
    string PacienteId,
    string MedicoId,
    DateTimeOffset FechaHora,
    string Motivo,
    string Estado);

/// <param name="Motivo">Resumen en español.</param>
/// <param name="MotivoEn">Resumen en inglés.</param>
/// <param name="MensajeOriginal">Lo que escribió el paciente, en su idioma.</param>
public sealed record Escalamiento(
    string Id,
    string SesionId,
    string? PacienteId,
    string Motivo,
    bool Urgente,
    string Estado,
    DateTimeOffset Creado,
    string? MotivoEn = null,
    string? MensajeOriginal = null,
    string? Idioma = null);

/// <summary>Datos para crear un escalamiento: resúmenes para el panel (es/en) y el mensaje original.</summary>
public sealed record NuevoEscalamiento(
    string SesionId,
    string? PacienteId,
    string ResumenEs,
    string? ResumenEn,
    bool Urgente,
    string? MensajeOriginal = null,
    string? Idioma = null);

/// <summary>Lo que el agente necesita saber del consultorio.</summary>
/// <param name="NombreAsistente">Nombre con el que se presenta el asistente; null usa <see cref="NombreAsistentePredeterminado"/>.</param>
public sealed record InfoConsultorio(string Nombre, string TelefonoEmergencias, string IdiomaPredeterminado, string? NombreAsistente = null)
{
    public const string NombreAsistentePredeterminado = "Clara";

    public string Asistente => string.IsNullOrWhiteSpace(NombreAsistente) ? NombreAsistentePredeterminado : NombreAsistente;

    /// <summary>Nombre del consultorio para los mensajes a pacientes.</summary>
    public string NombreVisible => string.IsNullOrWhiteSpace(Nombre) ? "DPM" : Nombre;
}

/// <summary>
/// Datos leídos de la foto de un carnet de ARS. Quedan en estado "pendiente" hasta que
/// el paciente o la secretaria los confirmen; solo entonces se aplican al paciente.
/// La imagen nunca se guarda.
/// </summary>
public sealed record CarnetLeido(
    string Id,
    string SesionId,
    string TelefonoCanal,
    string? PacienteId,
    DatosCarnet Datos,
    string Estado,
    DateTimeOffset Creado,
    string? ResueltoPor = null,
    DateTimeOffset? Resuelto = null);

/// <summary>
/// Puerta de entrada a los datos del consultorio. <see cref="DbDpmGateway"/> los guarda con EF Core;
/// <see cref="MockDpmGateway"/> los tiene en memoria para las pruebas.
/// </summary>
public interface IDpmGateway
{
    Task<Paciente?> BuscarPacienteAsync(string telefonoOId, CancellationToken ct);

    /// <summary>Todos los pacientes vinculados a un teléfono (una familia: el titular, sus hijos, sus padres), por id.</summary>
    Task<IReadOnlyList<Paciente>> PacientesDelTelefonoAsync(string telefono, CancellationToken ct);

    Task<Paciente> RegistrarPacienteAsync(string nombre, string telefono, string? ars, CancellationToken ct);

    Task<IReadOnlyList<Medico>> ListarMedicosAsync(CancellationToken ct);

    Task<IReadOnlyList<DateTimeOffset>> ConsultarDisponibilidadAsync(string medicoId, DateOnly fecha, CancellationToken ct);

    Task<Cita> CrearCitaAsync(string pacienteId, string medicoId, DateTimeOffset fechaHora, string motivo, CancellationToken ct);

    Task<IReadOnlyList<Cita>> CitasDePacienteAsync(string pacienteId, CancellationToken ct);

    /// <summary>
    /// Cancela una cita solo si pertenece al paciente indicado.
    /// Devuelve null si la cita no existe o es de otro paciente (no se distingue a propósito).
    /// </summary>
    Task<Cita?> CancelarCitaAsync(string citaId, string pacienteId, CancellationToken ct);

    Task<Escalamiento> RegistrarEscalamientoAsync(NuevoEscalamiento escalamiento, CancellationToken ct);

    /// <summary>Escalamiento con un solo resumen (sin mensaje original): para avisos internos.</summary>
    Task<Escalamiento> RegistrarEscalamientoAsync(string sesionId, string? pacienteId, string motivo, bool urgente, CancellationToken ct) =>
        RegistrarEscalamientoAsync(new NuevoEscalamiento(sesionId, pacienteId, motivo, null, urgente), ct);

    Task<InfoConsultorio> ObtenerConsultorioAsync(CancellationToken ct);

    Task<IReadOnlyList<Escalamiento>> ListarEscalamientosAsync(CancellationToken ct);

    /// <summary>Actualiza la ARS y el número de afiliado. Los valores null no borran lo que ya había.</summary>
    Task<Paciente> ActualizarSeguroAsync(string pacienteId, string? ars, string? numeroAfiliado, CancellationToken ct);

    Task<CarnetLeido> GuardarCarnetPendienteAsync(
        string sesionId, string telefonoCanal, string? pacienteId, DatosCarnet datos, CancellationToken ct);

    Task<CarnetLeido?> ObtenerCarnetAsync(string carnetId, CancellationToken ct);

    /// <summary>El carnet pendiente más reciente de la sesión, si hay.</summary>
    Task<CarnetLeido?> UltimoCarnetPendienteAsync(string sesionId, CancellationToken ct);

    Task<IReadOnlyList<CarnetLeido>> ListarCarnetsPendientesAsync(CancellationToken ct);

    /// <summary>
    /// Marca un carnet pendiente como confirmado o descartado. Falla con <see cref="InvalidOperationException"/>
    /// si ya no estaba pendiente, para que dos personas no lo resuelvan a la vez.
    /// </summary>
    Task<CarnetLeido> ResolverCarnetAsync(
        string carnetId, string estado, DatosCarnet datos, string? pacienteId, string resueltoPor, CancellationToken ct);
}
