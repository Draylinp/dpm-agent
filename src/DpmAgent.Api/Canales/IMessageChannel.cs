namespace DpmAgent.Api.Canales;

/// <summary>Mensaje listo para entregar por un canal.</summary>
public sealed record MensajeParaEnviar(long Id, string SesionId, string Destino, string Texto, string Tipo);

/// <summary>Canal por el que se entregan los mensajes que el sistema envía por iniciativa propia.</summary>
public interface IMessageChannel
{
    /// <summary>Nombre guardado en cada mensaje saliente: "web" o "whatsapp".</summary>
    string Canal { get; }

    Task EnviarAsync(MensajeParaEnviar mensaje, CancellationToken ct);
}

public static class CanalesConocidos
{
    public const string Web = "web";
    public const string WhatsApp = "whatsapp";

    /// <summary>Prefijo de las sesiones que vienen de WhatsApp: "wa-" seguido del número.</summary>
    public const string PrefijoSesionWhatsApp = "wa-";

    public static string DeSesion(string sesionId) =>
        sesionId.StartsWith(PrefijoSesionWhatsApp, StringComparison.Ordinal) ? WhatsApp : Web;

    public static string SesionWhatsApp(string telefono) => PrefijoSesionWhatsApp + telefono;
}

/// <summary>
/// Chat web del panel: no hay a quién empujarle el mensaje; el panel lo recoge
/// consultando GET /api/chat/{sesionId}/salientes.
/// </summary>
public sealed class WebChatChannel : IMessageChannel
{
    public string Canal => CanalesConocidos.Web;

    public Task EnviarAsync(MensajeParaEnviar mensaje, CancellationToken ct) => Task.CompletedTask;
}
