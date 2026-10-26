namespace DpmAgent.Api.Data.Entidades;

// Todas las tablas llevan ConsultorioId: un mismo despliegue atiende varios consultorios
// y DpmDbContext filtra cada consulta por el consultorio actual.
// Las fechas se guardan en UTC.

public sealed class Consultorio
{
    public int Id { get; set; }

    public required string Nombre { get; set; }

    /// <summary>Número de WhatsApp Business del consultorio, para mostrar.</summary>
    public string? TelefonoWhatsApp { get; set; }

    /// <summary>phone_number_id de WhatsApp Cloud API: identifica a qué consultorio llega cada mensaje.</summary>
    public string? WhatsAppPhoneNumberId { get; set; }

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

    /// <summary>Cuándo se envió el recordatorio de 24 horas.</summary>
    public DateTime? RecordatorioEnviadoUtc { get; set; }

    /// <summary>Cuándo el paciente confirmó que asistirá.</summary>
    public DateTime? AsistenciaConfirmadaUtc { get; set; }

    /// <summary>En una cita cancelada: cuándo terminó el intento de llenar el hueco con la lista de espera.</summary>
    public DateTime? HuecoProcesadoUtc { get; set; }
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

    /// <summary>activa, atendida o cancelada.</summary>
    public required string Estado { get; set; }

    public DateTime CreadaUtc { get; set; }
}

/// <summary>Hueco liberado que se le ofrece a un paciente de la lista de espera.</summary>
public sealed class OfertaHueco
{
    public int Id { get; set; }

    public int ConsultorioId { get; set; }

    public int ListaEsperaId { get; set; }

    public int PacienteId { get; set; }

    public int MedicoId { get; set; }

    public DateTime FechaHoraUtc { get; set; }

    /// <summary>pendiente, aceptada, rechazada, vencida o invalida (el hueco ya no estaba libre).</summary>
    public required string Estado { get; set; }

    public DateTime CreadaUtc { get; set; }

    public DateTime VenceUtc { get; set; }

    public DateTime? RespondidaUtc { get; set; }

    /// <summary>Cita creada al aceptar.</summary>
    public int? CitaId { get; set; }
}

/// <summary>
/// Mensaje que el sistema envía por iniciativa propia (oferta, recordatorio).
/// Se encola y un despachador lo entrega por el canal que corresponde.
/// </summary>
public sealed class MensajeSaliente
{
    public long Id { get; set; }

    public int ConsultorioId { get; set; }

    public required string SesionId { get; set; }

    /// <summary>web o whatsapp.</summary>
    public required string Canal { get; set; }

    public required string Destino { get; set; }

    public required string Texto { get; set; }

    /// <summary>oferta, recordatorio, aviso o respuesta.</summary>
    public required string Tipo { get; set; }

    /// <summary>Clave de la plantilla de WhatsApp a usar fuera de la ventana de 24 horas (oferta, recordatorio...).</summary>
    public string? Plantilla { get; set; }

    /// <summary>Parámetros de la plantilla, como arreglo JSON de textos.</summary>
    public string? ParametrosJson { get; set; }

    /// <summary>pendiente, enviado o fallido.</summary>
    public required string Estado { get; set; }

    public int Intentos { get; set; }

    public string? Error { get; set; }

    public DateTime CreadoUtc { get; set; }

    public DateTime? EnviadoUtc { get; set; }
}

/// <summary>
/// Mensaje recibido por WhatsApp. El webhook solo lo guarda (para responder rápido a Meta y no
/// procesar dos veces un reintento); un procesador en segundo plano lo pasa al agente.
/// </summary>
public sealed class MensajeEntrante
{
    public long Id { get; set; }

    public int ConsultorioId { get; set; }

    /// <summary>Id del mensaje en WhatsApp (wamid). Único: los reintentos de Meta se ignoran.</summary>
    public required string ProveedorId { get; set; }

    public required string Telefono { get; set; }

    /// <summary>text, audio, image u otro.</summary>
    public required string Tipo { get; set; }

    public string? Texto { get; set; }

    public string? MediaId { get; set; }

    /// <summary>pendiente, procesado o fallido.</summary>
    public required string Estado { get; set; }

    public int Intentos { get; set; }

    public string? Error { get; set; }

    public DateTime RecibidoUtc { get; set; }

    public DateTime? ProcesadoUtc { get; set; }
}

/// <summary>Bitácora: quién hizo qué y cuándo. Sin detalles clínicos.</summary>
public sealed class RegistroBitacora
{
    public long Id { get; set; }

    public int ConsultorioId { get; set; }

    public DateTime FechaUtc { get; set; }

    /// <summary>agente, sistema o secretaria:usuario.</summary>
    public required string Actor { get; set; }

    public required string Accion { get; set; }

    public string? Entidad { get; set; }

    public string? EntidadId { get; set; }

    public string? SesionId { get; set; }

    public string? Detalle { get; set; }
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
