using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DpmAgent.Api.Canales.WhatsApp;

/// <summary>Un mensaje recibido, ya extraído del JSON del webhook.</summary>
public sealed record MensajeWhatsApp(
    string PhoneNumberId, string ProveedorId, string Telefono, string Tipo, string? Texto, string? MediaId);

/// <summary>Validación de la firma y lectura del cuerpo del webhook de WhatsApp Cloud API.</summary>
public static class WebhookWhatsApp
{
    /// <summary>
    /// Compara X-Hub-Signature-256 ("sha256=" + HMAC-SHA256 del cuerpo con el secreto de la app)
    /// en tiempo constante.
    /// </summary>
    public static bool FirmaValida(byte[] cuerpo, string? encabezado, string appSecret)
    {
        const string prefijo = "sha256=";
        if (string.IsNullOrWhiteSpace(encabezado) || !encabezado.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] recibida;
        try
        {
            recibida = Convert.FromHexString(encabezado[prefijo.Length..].Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        var esperada = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), cuerpo);
        return CryptographicOperations.FixedTimeEquals(esperada, recibida);
    }

    public static string Firmar(byte[] cuerpo, string appSecret) =>
        "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), cuerpo)).ToLowerInvariant();

    /// <summary>
    /// Extrae los mensajes del cuerpo. Ignora estados de entrega (sent, delivered, read) y todo lo que no entienda.
    /// </summary>
    public static IReadOnlyList<MensajeWhatsApp> LeerMensajes(byte[] cuerpo)
    {
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(cuerpo);
        }
        catch (JsonException)
        {
            return [];
        }

        var lista = new List<MensajeWhatsApp>();
        foreach (var entry in raiz?["entry"]?.AsArray() ?? [])
        {
            foreach (var change in entry?["changes"]?.AsArray() ?? [])
            {
                var value = change?["value"];
                var phoneNumberId = value?["metadata"]?["phone_number_id"]?.ToString();
                if (phoneNumberId is null)
                    continue;

                foreach (var m in value?["messages"]?.AsArray() ?? [])
                {
                    var id = m?["id"]?.ToString();
                    var de = m?["from"]?.ToString();
                    var tipo = m?["type"]?.ToString();
                    if (id is null || de is null || tipo is null)
                        continue;

                    lista.Add(tipo switch
                    {
                        "text" => new(phoneNumberId, id, de, "text", m!["text"]?["body"]?.ToString(), null),
                        "audio" => new(phoneNumberId, id, de, "audio", null, m!["audio"]?["id"]?.ToString()),
                        "image" => new(phoneNumberId, id, de, "image", m!["image"]?["caption"]?.ToString(), m["image"]?["id"]?.ToString()),
                        // Respuesta con botón de una plantilla (por ejemplo "SÍ" o "CONFIRMO").
                        "button" => new(phoneNumberId, id, de, "text", m!["button"]?["text"]?.ToString(), null),
                        "interactive" => new(phoneNumberId, id, de, "text",
                            m!["interactive"]?["button_reply"]?["title"]?.ToString() ?? m["interactive"]?["list_reply"]?["title"]?.ToString(), null),
                        _ => new(phoneNumberId, id, de, "otro", null, null),
                    });
                }
            }
        }

        return lista;
    }
}
