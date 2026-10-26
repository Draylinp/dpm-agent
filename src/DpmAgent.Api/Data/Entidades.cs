namespace DpmAgent.Api.Data.Entidades;

// Todas las tablas llevan ConsultorioId: un mismo despliegue atiende varios consultorios
// y DpmDbContext filtra cada consulta por el consultorio actual.
// Las fechas se guardan en UTC.

public sealed class Consultorio
{
    public int Id { get; set; }

    public required string Nombre { get; set; }

    /// <summary>Número de WhatsApp Business del consultorio (se usa en F5 para enrutar mensajes).</summary>
    public string? TelefonoWhatsApp { get; set; }

    public DateTime CreadoUtc { get; set; }
}

public sealed class Medico
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public required string Nombre { get; set; }

    public required string Especialidad { get; set; }

    public bool Activo { get; set; } = true;
}

public sealed class Paciente
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public required string Nombre { get; set; }

    /// <summary>Normalizado a 10 dígitos con <see cref="Dpm.Telefono.Normalizar"/>.</summary>
    public required string Telefono { get; set; }

    public string? Ars { get; set; }

    public string? NumeroAfiliado { get; set; }

    public DateTime CreadoUtc { get; set; }
}

public sealed class Cita
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public int PacienteId { get; set; }

    public Paciente? Paciente { get; set; }

    public int MedicoId { get; set; }

    public Medico? Medico { get; set; }

    public DateTime FechaHoraUtc { get; set; }

    public required string Motivo { get; set; }

    public required string Estado { get; set; }

    public DateTime CreadaUtc { get; set; }

    public DateTime? CanceladaUtc { get; set; }
}

/// <summary>Paciente que espera un hueco con un médico en un rango de días y horas (F4).</summary>
public sealed class ListaEspera
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public int PacienteId { get; set; }

    public Paciente? Paciente { get; set; }

    public int MedicoId { get; set; }

    public Medico? Medico { get; set; }

    public DateOnly Desde { get; set; }

    public DateOnly Hasta { get; set; }

    public TimeOnly HoraDesde { get; set; }

    public TimeOnly HoraHasta { get; set; }

    public required string Estado { get; set; }

    public DateTime CreadaUtc { get; set; }
}

public sealed class Escalamiento
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public required string SesionId { get; set; }

    public int? PacienteId { get; set; }

    public required string Motivo { get; set; }

    public bool Urgente { get; set; }

    public required string Estado { get; set; }

    public DateTime CreadoUtc { get; set; }

    public DateTime? AtendidoUtc { get; set; }
}

/// <summary>
/// Datos leídos de la foto de un carnet de ARS, pendientes de confirmación.
/// La imagen no se guarda: solo el resultado de la lectura.
/// </summary>
public sealed class CarnetPendiente
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public required string SesionId { get; set; }

    public required string TelefonoCanal { get; set; }

    public int? PacienteId { get; set; }

    public string? Nombre { get; set; }

    public string? Ars { get; set; }

    public string? NumeroAfiliado { get; set; }

    /// <summary>pendiente, confirmado o descartado.</summary>
    public required string Estado { get; set; }

    public DateTime CreadoUtc { get; set; }

    /// <summary>paciente o secretaria.</summary>
    public string? ResueltoPor { get; set; }

    public DateTime? ResueltoUtc { get; set; }
}

public sealed class Conversacion
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public required string SesionId { get; set; }

    public string? TelefonoCanal { get; set; }

    public int? PacienteId { get; set; }

    public DateTime CreadaUtc { get; set; }

    public DateTime ActualizadaUtc { get; set; }

    public List<Mensaje> Mensajes { get; set; } = [];
}

/// <summary>Un mensaje del historial (usuario, asistente o resultado de herramienta) en formato JSON de chat.</summary>
public sealed class Mensaje
{
    public long Id { get; set; }

    public int ConversacionId { get; set; }

    public required string Rol { get; set; }

    public required string Json { get; set; }

    public DateTime CreadoUtc { get; set; }
}
