using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Idiomas;

/// <summary>
/// Textos fijos del sistema en español, inglés y kreyòl (archivos Recursos/textos.{idioma}.json
/// incrustados en el ensamblado). Para cualquier otro idioma se usa inglés.
/// </summary>
public static class Textos
{
    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> PorIdioma = Cargar();

    private static readonly string[] DiasKreyol = ["dimanch", "lendi", "madi", "mèkredi", "jedi", "vandredi", "samdi"];

    private static readonly string[] MesesKreyol =
        ["janvye", "fevriye", "mas", "avril", "me", "jen", "jiyè", "out", "septanm", "oktòb", "novanm", "desanm"];

    public static string Obtener(string clave, string? idioma, params object[] argumentos)
    {
        var codigo = Idiomas.ParaTextos(idioma);
        if (!PorIdioma[codigo].TryGetValue(clave, out var plantilla) && !PorIdioma[Idiomas.Respaldo].TryGetValue(clave, out plantilla))
            throw new KeyNotFoundException($"No existe el texto '{clave}'.");

        return argumentos.Length == 0 ? plantilla : string.Format(Cultura(codigo), plantilla, argumentos);
    }

    public static IReadOnlyCollection<string> Claves(string idioma) => PorIdioma[idioma].Keys;

    /// <summary>"martes 27 de octubre", "Tuesday, October 27", "madi 27 oktòb".</summary>
    public static string Dia(DateTimeOffset momento, string? idioma)
    {
        var local = TimeZoneInfo.ConvertTime(momento, HorarioConsultorio.ZonaRd);
        return Idiomas.ParaTextos(idioma) switch
        {
            Idiomas.Kreyol => $"{DiasKreyol[(int)local.DayOfWeek]} {local.Day} {MesesKreyol[local.Month - 1]}",
            Idiomas.Ingles => local.ToString("dddd, MMMM d", Cultura(Idiomas.Ingles)),
            _ => local.ToString("dddd d 'de' MMMM", Cultura(Idiomas.Espanol)),
        };
    }

    /// <summary>"9:00 a. m." en español; "9:00 AM" en inglés y kreyòl.</summary>
    public static string Hora(DateTimeOffset momento, string? idioma)
    {
        var local = TimeZoneInfo.ConvertTime(momento, HorarioConsultorio.ZonaRd);
        return Idiomas.ParaTextos(idioma) == Idiomas.Espanol
            ? local.ToString("h:mm tt", Cultura(Idiomas.Espanol))
            : local.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
    }

    private static CultureInfo Cultura(string idioma) => idioma switch
    {
        Idiomas.Espanol => CultureInfo.GetCultureInfo("es-DO"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    private static FrozenDictionary<string, FrozenDictionary<string, string>> Cargar()
    {
        var ensamblado = Assembly.GetExecutingAssembly();
        var resultado = new Dictionary<string, FrozenDictionary<string, string>>();
        foreach (var idioma in Idiomas.Garantizados)
        {
            var nombre = $"DpmAgent.Api.Idiomas.Recursos.textos.{idioma}.json";
            using var stream = ensamblado.GetManifestResourceStream(nombre)
                               ?? throw new InvalidOperationException($"Falta el recurso {nombre}.");
            var textos = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                         ?? throw new InvalidOperationException($"El recurso {nombre} está vacío.");
            resultado[idioma] = textos.ToFrozenDictionary();
        }

        return resultado.ToFrozenDictionary();
    }
}
