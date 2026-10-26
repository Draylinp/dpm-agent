namespace DpmAgent.Api.Idiomas;

/// <summary>
/// Idiomas del agente. Nivel 1 (garantizado): español, inglés y kreyòl ayisyen, con textos,
/// plantillas y casos de evaluación propios. Nivel 2: cualquier otro; el agente responde en él
/// lo mejor posible y los textos fijos usan inglés como respaldo.
/// </summary>
public static class Idiomas
{
    public const string Espanol = "es";
    public const string Ingles = "en";
    public const string Kreyol = "ht";

    /// <summary>Idioma de respaldo para nivel 2.</summary>
    public const string Respaldo = Ingles;

    public static readonly IReadOnlyList<string> Garantizados = [Espanol, Ingles, Kreyol];

    private static readonly Dictionary<string, string> Nombres = new()
    {
        ["es"] = "español",
        ["en"] = "English",
        ["ht"] = "Kreyòl ayisyen",
        ["fr"] = "français",
        ["pt"] = "português",
        ["it"] = "italiano",
        ["de"] = "Deutsch",
    };

    /// <summary>Código BCP-47 en minúsculas, solo el idioma ("es-DO" → "es"). Null si no es válido.</summary>
    public static string? Normalizar(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            return null;

        var idioma = codigo.Trim().Split('-', '_')[0].ToLowerInvariant();
        return idioma.Length is 2 or 3 && idioma.All(char.IsAsciiLetterLower) ? idioma : null;
    }

    public static int Nivel(string? codigo) => Garantizados.Contains(Normalizar(codigo) ?? "") ? 1 : 2;

    /// <summary>
    /// Idioma en el que hay textos fijos: el mismo si es de nivel 1; inglés para nivel 2;
    /// español si todavía no se sabe.
    /// </summary>
    public static string ParaTextos(string? codigo) => Normalizar(codigo) switch
    {
        null => Espanol,
        var c when Garantizados.Contains(c) => c,
        _ => Respaldo,
    };

    public static string Nombre(string? codigo) =>
        Normalizar(codigo) is { } c && Nombres.TryGetValue(c, out var nombre) ? nombre : codigo ?? "?";
}
