namespace DpmAgent.Api.Canales.WhatsApp;

/// <summary>
/// Configuración de WhatsApp Cloud API (sección "WhatsApp"). Los secretos van por variables de entorno:
/// WhatsApp__AccessToken, WhatsApp__AppSecret y WhatsApp__VerifyToken.
/// </summary>
public sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    public string GraphBaseUrl { get; set; } = "https://graph.facebook.com/";

    public string ApiVersion { get; set; } = "v21.0";

    /// <summary>Token de acceso del usuario del sistema de la app de Meta.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Secreto de la app: valida la firma X-Hub-Signature-256 de cada webhook.</summary>
    public string? AppSecret { get; set; }

    /// <summary>Texto que se configura en Meta al suscribir el webhook (hub.verify_token).</summary>
    public string? VerifyToken { get; set; }

    /// <summary>phone_number_id del consultorio predeterminado, si no está guardado en la tabla de consultorios.</summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>
    /// Plantillas aprobadas en Meta para los mensajes que inicia el consultorio, por clave
    /// (oferta, oferta_vencida, recordatorio). Sin plantilla se envía texto, que solo llega
    /// si el paciente escribió en las últimas 24 horas.
    /// </summary>
    public Dictionary<string, string> Plantillas { get; set; } = [];

    public string IdiomaPlantillas { get; set; } = "es";

    public bool ProcesadorActivo { get; set; } = true;

    public int MaxIntentos { get; set; } = 3;

    public int TimeoutSeconds { get; set; } = 30;
}
