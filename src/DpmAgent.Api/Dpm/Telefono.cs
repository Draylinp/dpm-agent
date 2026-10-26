namespace DpmAgent.Api.Dpm;

/// <summary>
/// Normaliza teléfonos dominicanos a 10 dígitos (809/829/849 + número),
/// sin importar si vienen con formato o con el código de país (1).
/// </summary>
public static class Telefono
{
    public static string? Normalizar(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var digitos = new string(valor.Where(char.IsDigit).ToArray());
        if (digitos.Length < 7)
            return null;

        return digitos.Length > 10 ? digitos[^10..] : digitos;
    }

    public static bool Iguales(string? a, string? b)
    {
        var na = Normalizar(a);
        return na is not null && na == Normalizar(b);
    }
}
