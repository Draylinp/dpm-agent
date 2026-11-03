using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Model;

/// <summary>
/// Cliente para el endpoint /chat/completions de la Meta Model API (Muse Spark).
/// </summary>
public sealed class MetaModelClient : ChatCompletionsClient
{
    private readonly MetaOptions _options;

    public MetaModelClient(HttpClient http, IOptions<MetaOptions> options, ILogger<MetaModelClient> logger, IRegistroUso? registro = null)
        : base(http, options.Value.BaseUrl, options.Value.TimeoutSeconds, logger, registro)
    {
        _options = options.Value;
    }

    public override string Proveedor => "Meta Model API";

    protected override string Modelo => _options.Model;

    protected override double Temperatura => _options.Temperature;

    protected override int MaxTokens => _options.MaxCompletionTokens;

    protected override string ParametroMaxTokens => "max_completion_tokens";

    protected override string? ClaveApi()
    {
        var apiKey = _options.ApiKey ?? Environment.GetEnvironmentVariable("META_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Falta la clave de la Meta Model API. Define META_API_KEY o Meta__ApiKey.");
        }

        return apiKey;
    }
}
