using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace DpmAgent.Api.Model;

/// <summary>
/// Cliente común para servidores con el endpoint /chat/completions compatible con OpenAI
/// (Meta Model API, vLLM, Ollama...). Cada proveedor indica su configuración.
/// </summary>
public abstract class ChatCompletionsClient : IChatModel
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly IRegistroUso? _registro;

    protected ChatCompletionsClient(HttpClient http, string baseUrl, int timeoutSeconds, ILogger logger, IRegistroUso? registro = null)
    {
        _http = http;
        _logger = logger;
        _registro = registro;
        _http.BaseAddress ??= new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    }

    /// <summary>Nombre para los logs y mensajes de error.</summary>
    public abstract string Proveedor { get; }

    protected abstract string Modelo { get; }

    protected abstract double Temperatura { get; }

    protected abstract int MaxTokens { get; }

    /// <summary>Nombre del parámetro de límite de tokens: Meta usa max_completion_tokens; vLLM y Ollama, max_tokens.</summary>
    protected abstract string ParametroMaxTokens { get; }

    /// <summary>Clave de API, o null si el servidor no la pide.</summary>
    protected abstract string? ClaveApi();

    public async Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = Modelo,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)m.DeepClone()).ToArray()),
            ["temperature"] = Temperatura,
            [ParametroMaxTokens] = MaxTokens,
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
        if (ClaveApi() is { } clave)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);

        var started = DateTime.UtcNow;
        using var response = await _http.SendAsync(request, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("{Proveedor} respondió {Status}: {Body}", Proveedor, (int)response.StatusCode, raw);
            throw new HttpRequestException($"{Proveedor} respondió {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = JsonNode.Parse(raw)?.AsObject()
                   ?? throw new HttpRequestException($"Respuesta vacía de {Proveedor}.");

        var message = json["choices"]?[0]?["message"]?.AsObject()
                      ?? throw new HttpRequestException($"La respuesta de {Proveedor} no trae choices[0].message.");

        var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
        var entrada = Tokens(json, "prompt_tokens");
        var salida = Tokens(json, "completion_tokens");
        _logger.LogInformation("{Proveedor} {Model} respondió en {Ms} ms (tokens: {Entrada} + {Salida})", Proveedor, Modelo, ms, entrada, salida);
        _registro?.Registrar(ContextoUso.Uso(Proveedor, Modelo, entrada, salida, ms));

        // Se clona para separarlo del documento original.
        return message.DeepClone().AsObject();
    }

    private static int Tokens(JsonObject respuesta, string campo) =>
        respuesta["usage"]?[campo] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
}
