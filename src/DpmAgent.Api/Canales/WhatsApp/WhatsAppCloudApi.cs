using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Canales.WhatsApp;

/// <summary>Archivo descargado de WhatsApp (nota de voz o foto).</summary>
public sealed record MediaDescargada(byte[] Datos, string? MimeType);

/// <summary>Las llamadas a WhatsApp Cloud API que usa el agente.</summary>
public interface IWhatsAppCloudApi
{
    Task EnviarTextoAsync(string phoneNumberId, string destino, string texto, CancellationToken ct);

    Task EnviarPlantillaAsync(
        string phoneNumberId, string destino, string plantilla, string idioma, IReadOnlyList<string> parametros, CancellationToken ct);

    /// <summary>Descarga un archivo recibido. Falla si pasa de <paramref name="maxBytes"/>.</summary>
    Task<MediaDescargada> DescargarMediaAsync(string mediaId, long maxBytes, CancellationToken ct);
}

/// <summary>
/// Cliente de WhatsApp Cloud API (Graph API): POST /{phone_number_id}/messages para enviar
/// y GET /{media_id} + GET de la URL firmada para descargar archivos.
/// </summary>
public sealed class WhatsAppCloudApi : IWhatsAppCloudApi
{
    private readonly HttpClient _http;
    private readonly WhatsAppOptions _options;
    private readonly ILogger<WhatsAppCloudApi> _logger;

    public WhatsAppCloudApi(HttpClient http, IOptions<WhatsAppOptions> options, ILogger<WhatsAppCloudApi> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        var baseUrl = _options.GraphBaseUrl.EndsWith('/') ? _options.GraphBaseUrl : _options.GraphBaseUrl + "/";
        _http.BaseAddress ??= new Uri(baseUrl + _options.ApiVersion.Trim('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public Task EnviarTextoAsync(string phoneNumberId, string destino, string texto, CancellationToken ct) =>
        EnviarAsync(phoneNumberId, new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = destino,
            ["type"] = "text",
            ["text"] = new JsonObject { ["preview_url"] = false, ["body"] = texto },
        }, ct);

    public Task EnviarPlantillaAsync(
        string phoneNumberId, string destino, string plantilla, string idioma, IReadOnlyList<string> parametros, CancellationToken ct)
    {
        var template = new JsonObject
        {
            ["name"] = plantilla,
            ["language"] = new JsonObject { ["code"] = idioma },
        };
        if (parametros.Count > 0)
        {
            template["components"] = new JsonArray(new JsonObject
            {
                ["type"] = "body",
                ["parameters"] = new JsonArray(parametros
                    .Select(p => (JsonNode)new JsonObject { ["type"] = "text", ["text"] = p })
                    .ToArray()),
            });
        }

        return EnviarAsync(phoneNumberId, new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["to"] = destino,
            ["type"] = "template",
            ["template"] = template,
        }, ct);
    }

    public async Task<MediaDescargada> DescargarMediaAsync(string mediaId, long maxBytes, CancellationToken ct)
    {
        // 1. Metadatos: URL temporal, tipo y tamaño.
        using var meta = Peticion(HttpMethod.Get, Uri.EscapeDataString(mediaId));
        using var respMeta = await _http.SendAsync(meta, ct);
        var json = await LeerJsonAsync(respMeta, "consultar el archivo", ct);

        var url = json["url"]?.ToString() ?? throw new InvalidOperationException("WhatsApp no devolvió la URL del archivo.");
        if (json["file_size"]?.GetValue<long>() is { } tamano && tamano > maxBytes)
            throw new ArchivoDemasiadoGrandeException();

        // 2. El archivo en sí, cortando si pasa del límite aunque el tamaño declarado mintiera.
        using var archivo = Peticion(HttpMethod.Get, url);
        using var respArchivo = await _http.SendAsync(archivo, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!respArchivo.IsSuccessStatusCode)
            throw new HttpRequestException($"WhatsApp respondió {(int)respArchivo.StatusCode} al descargar el archivo.", null, respArchivo.StatusCode);

        await using var stream = await respArchivo.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bloque = new byte[81920];
        int leidos;
        while ((leidos = await stream.ReadAsync(bloque, ct)) > 0)
        {
            if (buffer.Length + leidos > maxBytes)
                throw new ArchivoDemasiadoGrandeException();
            buffer.Write(bloque, 0, leidos);
        }

        return new MediaDescargada(buffer.ToArray(), json["mime_type"]?.ToString());
    }

    private async Task EnviarAsync(string phoneNumberId, JsonObject cuerpo, CancellationToken ct)
    {
        using var request = Peticion(HttpMethod.Post, $"{Uri.EscapeDataString(phoneNumberId)}/messages");
        request.Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, ct);
        await LeerJsonAsync(response, "enviar el mensaje", ct);
    }

    private HttpRequestMessage Peticion(HttpMethod metodo, string ruta)
    {
        if (string.IsNullOrWhiteSpace(_options.AccessToken))
            throw new InvalidOperationException("WhatsApp no está configurado: falta WhatsApp__AccessToken.");

        var request = new HttpRequestMessage(metodo, ruta);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        return request;
    }

    private async Task<JsonObject> LeerJsonAsync(HttpResponseMessage response, string accion, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            // El mensaje de error de Meta no trae datos del paciente: se puede registrar.
            var detalle = TryParse(raw)?["error"]?["message"]?.ToString() ?? raw;
            _logger.LogError("WhatsApp respondió {Status} al {Accion}: {Detalle}", (int)response.StatusCode, accion, detalle);
            throw new HttpRequestException($"WhatsApp respondió {(int)response.StatusCode} al {accion}: {detalle}", null, response.StatusCode);
        }

        return TryParse(raw) ?? new JsonObject();
    }

    private static JsonObject? TryParse(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

public sealed class ArchivoDemasiadoGrandeException()
    : Agent.EntradaRechazadaException("El archivo es demasiado grande. Envíe uno más pequeño.", StatusCodes.Status413PayloadTooLarge);
