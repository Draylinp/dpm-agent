using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Model;

/// <summary>
/// Configuración de un servidor propio compatible con OpenAI (sección "OpenAICompatible"):
/// Muse Glimmer servido con vLLM en Modal o Baseten, o en local con vLLM u Ollama.
/// </summary>
public sealed class OpenAICompatibleOptions
{
    public const string SectionName = "OpenAICompatible";

    /// <summary>Por ejemplo https://usuario--dpm-glimmer-serve.modal.run/v1/ o http://localhost:11434/v1/.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000/v1/";

    /// <summary>Nombre con el que el servidor expone el modelo (el id de Hugging Face en vLLM).</summary>
    public string Model { get; set; } = "";

    /// <summary>Clave del servidor (vLLM --api-key). Vacía si el servidor no la pide.</summary>
    public string? ApiKey { get; set; }

    public int MaxTokens { get; set; } = 1024;

    public double Temperature { get; set; } = 0.2;

    /// <summary>Un 30B en una GPU con arranque en frío puede tardar: margen amplio.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>Modelo servido por un endpoint /chat/completions propio (Muse Glimmer con pesos abiertos).</summary>
public sealed class OpenAICompatibleChatModel : ChatCompletionsClient
{
    private readonly OpenAICompatibleOptions _options;

    public OpenAICompatibleChatModel(
        HttpClient http, IOptions<OpenAICompatibleOptions> options, ILogger<OpenAICompatibleChatModel> logger, IRegistroUso? registro = null)
        : base(http, options.Value.BaseUrl, options.Value.TimeoutSeconds, logger, registro)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("Falta OpenAICompatible:Model (el nombre del modelo en el servidor).");
    }

    public override string Proveedor => "Servidor OpenAI-compatible";

    protected override string Modelo => _options.Model;

    protected override double Temperatura => _options.Temperature;

    protected override int MaxTokens => _options.MaxTokens;

    protected override string ParametroMaxTokens => "max_tokens";

    protected override string? ClaveApi() => string.IsNullOrWhiteSpace(_options.ApiKey) ? null : _options.ApiKey;
}
