namespace DpmAgent.Api.Model;

/// <summary>
/// Configuración de la Meta Model API.
/// Se lee de la sección "Meta" de appsettings o de variables de entorno
/// (Meta__ApiKey, Meta__Model, Meta__BaseUrl). También acepta META_API_KEY.
/// </summary>
public sealed class MetaOptions
{
    public const string SectionName = "Meta";

    /// <summary>Endpoint base compatible con OpenAI.</summary>
    public string BaseUrl { get; set; } = "https://api.meta.ai/v1/";

    /// <summary>Modelo de Meta a usar. Debe soportar tool calling.</summary>
    public string Model { get; set; } = "muse-spark-1.2";

    /// <summary>Clave de API. Nunca se guarda en el repositorio.</summary>
    public string? ApiKey { get; set; }

    public int MaxCompletionTokens { get; set; } = 1024;

    public double Temperature { get; set; } = 0.2;

    public int TimeoutSeconds { get; set; } = 60;
}
