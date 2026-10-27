using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using DpmAgent.Api.Model;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Voz;

/// <summary>
/// Transcribe notas de voz con Muse Voice Transcribe por la Meta Model API.
/// Envía multipart/form-data con los campos <c>file</c>, <c>model</c> y <c>language</c>
/// y lee <c>text</c> (y <c>duration</c> si viene) de la respuesta, como el endpoint de
/// transcripción compatible con OpenAI. La ruta y el modelo se configuran en "Transcripcion".
/// </summary>
public sealed class MetaTranscriber : ITranscriber
{
    private readonly HttpClient _http;
    private readonly MetaOptions _meta;
    private readonly TranscripcionOptions _options;
    private readonly ILogger<MetaTranscriber> _logger;

    public MetaTranscriber(
        HttpClient http,
        IOptions<MetaOptions> meta,
        IOptions<TranscripcionOptions> options,
        ILogger<MetaTranscriber> logger)
    {
        _http = http;
        _meta = meta.Value;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress ??= new Uri(_meta.BaseUrl.EndsWith('/') ? _meta.BaseUrl : _meta.BaseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<Transcripcion> TranscribirAsync(AudioRecibido audio, CancellationToken ct)
    {
        var apiKey = _meta.ApiKey ?? Environment.GetEnvironmentVariable("META_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Falta la clave de la Meta Model API. Define META_API_KEY o Meta__ApiKey.");
        }

        var archivo = new ByteArrayContent(audio.Datos);
        archivo.Headers.ContentType = new MediaTypeHeaderValue(audio.ContentType);

        using var form = new MultipartFormDataContent
        {
            { archivo, "file", audio.NombreArchivo },
            { new StringContent(_options.Modelo), "model" },
        };
        if (!string.IsNullOrWhiteSpace(_options.Idioma))
            form.Add(new StringContent(_options.Idioma), "language");

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Ruta.TrimStart('/')) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var started = DateTime.UtcNow;
        using var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // No se registra el cuerpo enviado: es la voz del paciente.
            _logger.LogError("Transcripción respondió {Status}: {Body}", (int)response.StatusCode, Seguridad.Privacidad.Enmascarar(raw));
            throw new HttpRequestException(
                $"El servicio de transcripción respondió {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = JsonNode.Parse(raw)?.AsObject()
                   ?? throw new InvalidOperationException("Respuesta vacía del servicio de transcripción.");

        var texto = json["text"]?.ToString()
                    ?? throw new InvalidOperationException("La respuesta de transcripción no trae 'text'.");

        double? duracion = json["duration"] is JsonValue v && double.TryParse(
            v.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

        _logger.LogInformation(
            "Transcripción {Modelo}: {Bytes} bytes {Formato} en {Ms} ms",
            _options.Modelo, audio.Datos.Length, audio.Formato, (int)(DateTime.UtcNow - started).TotalMilliseconds);

        return new Transcripcion(texto.Trim(), duracion);
    }
}
