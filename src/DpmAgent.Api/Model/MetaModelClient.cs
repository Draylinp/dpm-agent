using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Model;

/// <summary>
/// Cliente para el endpoint /chat/completions de la Meta Model API.
/// </summary>
public sealed class MetaModelClient : IChatModel
{
    private readonly HttpClient _http;
    private readonly MetaOptions _options;
    private readonly ILogger<MetaModelClient> _logger;

    public MetaModelClient(HttpClient http, IOptions<MetaOptions> options, ILogger<MetaModelClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _http.BaseAddress ??= new Uri(_options.BaseUrl.EndsWith('/') ? _options.BaseUrl : _options.BaseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public async Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
    {
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("META_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Falta la clave de la Meta Model API. Define META_API_KEY o Meta__ApiKey.");
        }

        var body = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)m.DeepClone()).ToArray()),
            ["temperature"] = _options.Temperature,
            ["max_completion_tokens"] = _options.MaxCompletionTokens,
        };

        if (tools.Count > 0)
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = "auto";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var started = DateTime.UtcNow;
        using var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Meta Model API respondió {Status}: {Body}", (int)response.StatusCode, raw);
            throw new HttpRequestException(
                $"Meta Model API respondió {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = JsonNode.Parse(raw)?.AsObject()
                   ?? throw new InvalidOperationException("Respuesta vacía de la Meta Model API.");

        var message = json["choices"]?[0]?["message"]?.AsObject()
                      ?? throw new InvalidOperationException("La respuesta no trae choices[0].message.");

        _logger.LogInformation(
            "Meta {Model} respondió en {Ms} ms (tokens: {Usage})",
            _options.Model,
            (int)(DateTime.UtcNow - started).TotalMilliseconds,
            json["usage"]?["total_tokens"]?.ToString() ?? "?");

        // Se clona para separarlo del documento original.
        return message.DeepClone().AsObject();
    }
}
