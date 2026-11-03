using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DpmAgent.Api.Dpm;

/// <summary>
/// Datos administrativos de un carnet de ARS. Vienen de una imagen (o de una corrección humana),
/// así que se limpian: longitud máxima y solo caracteres esperados. Lo que no pasa queda en null.
/// </summary>
public sealed partial record DatosCarnet(string? Nombre, string? Ars, string? NumeroAfiliado)
{
    public const int MaxNombre = 100;
    public const int MaxArs = 60;
    public const int MaxAfiliado = 30;

    [JsonIgnore]
    public bool Vacio => Nombre is null && Ars is null && NumeroAfiliado is null;

    public static DatosCarnet Limpiar(string? nombre, string? ars, string? numeroAfiliado) => new(
        Limpiar(nombre, MaxNombre, NombreValido()),
        Limpiar(ars, MaxArs, ArsValida()),
        Limpiar(numeroAfiliado, MaxAfiliado, AfiliadoValido()));

    /// <summary>Aplica encima los campos de la corrección que vengan con valor.</summary>
    public DatosCarnet Corregir(DatosCarnet? correccion) => correccion is null
        ? this
        : new(correccion.Nombre ?? Nombre, correccion.Ars ?? Ars, correccion.NumeroAfiliado ?? NumeroAfiliado);

    /// <summary>
    /// Compara el nombre del carnet con el de un paciente ya registrado, sin acentos ni mayúsculas.
    /// Coinciden si comparten al menos dos palabras (o todas, si uno tiene una sola).
    /// </summary>
    public static bool NombresCoinciden(string? a, string? b)
    {
        var pa = Palabras(a);
        var pb = Palabras(b);
        if (pa.Count == 0 || pb.Count == 0)
            return false;

        var comunes = pa.Intersect(pb).Count();
        return comunes >= Math.Min(2, Math.Min(pa.Count, pb.Count));
    }

    private static HashSet<string> Palabras(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
            return [];

        var sinMarca = nombre.Replace("(ficticio)", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("(ficticia)", " ", StringComparison.OrdinalIgnoreCase);
        var sinAcentos = new string(sinMarca.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .ToArray());

        return sinAcentos.ToLowerInvariant()
            .Split(new[] { ' ', '-', '.', '\'' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length > 1 && p is not ("de" or "del" or "la" or "los" or "las"))
            .ToHashSet();
    }

    private static string? Limpiar(string? valor, int max, Regex permitido)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto = Espacios().Replace(valor.Trim(), " ");
        return texto.Length <= max && permitido.IsMatch(texto) ? texto : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacios();

    [GeneratedRegex(@"^[\p{L}][\p{L} .'\-()]*$")]
    private static partial Regex NombreValido();

    [GeneratedRegex(@"^[\p{L}\d][\p{L}\d .&\-]*$")]
    private static partial Regex ArsValida();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\-]*$")]
    private static partial Regex AfiliadoValido();
}
