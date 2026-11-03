namespace DpmAgent.Api.Model;

/// <summary>Elección del modelo del agente (sección "Modelo").</summary>
public sealed class ModeloOptions
{
    public const string SectionName = "Modelo";

    /// <summary>MetaApi (Muse Spark por la Meta Model API) u OpenAICompatible (Muse Glimmer en servidor propio).</summary>
    public string Proveedor { get; set; } = "MetaApi";

    /// <summary>Proveedor de respaldo si el principal falla. Vacío: sin respaldo.</summary>
    public string? Respaldo { get; set; }
}

public static class ModelosExtensions
{
    public const string MetaApi = "metaapi";
    public const string OpenAICompatible = "openaicompatible";

    public static IServiceCollection AddModelos(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MetaOptions>(config.GetSection(MetaOptions.SectionName));
        services.Configure<OpenAICompatibleOptions>(config.GetSection(OpenAICompatibleOptions.SectionName));
        services.AddHttpClient<MetaModelClient>();
        services.AddHttpClient<OpenAICompatibleChatModel>();

        var opciones = config.GetSection(ModeloOptions.SectionName).Get<ModeloOptions>() ?? new ModeloOptions();
        var principal = Normalizar(opciones.Proveedor);
        var respaldo = string.IsNullOrWhiteSpace(opciones.Respaldo) ? null : Normalizar(opciones.Respaldo);

        VerificarPrivacidad(config);

        services.AddTransient<IChatModel>(sp =>
        {
            var modelo = Crear(sp, principal);
            return respaldo is null || respaldo == principal
                ? modelo
                : new ModeloConRespaldo(modelo, Crear(sp, respaldo), sp.GetRequiredService<ILogger<ModeloConRespaldo>>());
        });

        return services;
    }

    /// <summary>
    /// Las variantes "-contributor" son más baratas, pero Meta usa los prompts para entrenar:
    /// con datos de pacientes no se permiten.
    /// </summary>
    internal static void VerificarPrivacidad(IConfiguration config)
    {
        foreach (var clave in new[] { "Meta:Model", "Carnet:Modelo", "Transcripcion:Modelo" })
        {
            var modelo = config[clave];
            if (modelo is not null && modelo.Contains("contributor", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"{clave} = '{modelo}': las variantes -contributor usan los prompts para entrenar y no se pueden usar con datos de pacientes.");
            }
        }
    }

    private static IChatModel Crear(IServiceProvider sp, string proveedor) => proveedor switch
    {
        MetaApi => sp.GetRequiredService<MetaModelClient>(),
        OpenAICompatible => sp.GetRequiredService<OpenAICompatibleChatModel>(),
        _ => throw new InvalidOperationException($"Proveedor de modelo desconocido: {proveedor}."),
    };

    private static string Normalizar(string proveedor) => proveedor.Trim().ToLowerInvariant() switch
    {
        MetaApi or "meta" => MetaApi,
        OpenAICompatible or "openai-compatible" or "glimmer" => OpenAICompatible,
        var otro => throw new InvalidOperationException($"Proveedor de modelo desconocido: {otro}. Use MetaApi u OpenAICompatible."),
    };
}
