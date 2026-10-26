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

    /// <summary>
    /// Formato internacional sin "+", como lo usa WhatsApp. Los números de 10 dígitos
    /// (República Dominicana y resto del plan norteamericano) llevan el código 1.
    /// </summary>
    public static string? Internacional(string? valor)
    {
        var digitos = valor is null ? "" : new string(valor.Where(char.IsDigit).ToArray());
        if (digitos.Length < 7)
            return null;
        return digitos.Length == 10 ? "1" + digitos : digitos;
    }

    public static bool Iguales(string? a, string? b)
    {
        var na = Normalizar(a);
        return na is not null && na == Normalizar(b);
    }
}
