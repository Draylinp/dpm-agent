using System.Security.Cryptography;
using System.Text;

namespace DpmAgent.Api.Panel;

/// <summary>
/// Códigos de un solo uso por tiempo (RFC 6238, como Google Authenticator o Microsoft Authenticator):
/// HMAC-SHA1, 6 dígitos, intervalos de 30 segundos, se acepta un intervalo antes o después por la hora del teléfono.
/// </summary>
public static class Totp
{
    private const int Digitos = 6;
    private const int SegundosPorPaso = 30;
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NuevoSecreto() => RandomNumberGenerator.GetBytes(20);

    public static long Paso(DateTimeOffset momento) => momento.ToUnixTimeSeconds() / SegundosPorPaso;

    public static string Codigo(byte[] secreto, long paso)
    {
        var contador = BitConverter.GetBytes(paso);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(contador);
        var hash = HMACSHA1.HashData(secreto, contador);
        var desplazamiento = hash[^1] & 0x0F;
        var valor = ((hash[desplazamiento] & 0x7F) << 24) | (hash[desplazamiento + 1] << 16) | (hash[desplazamiento + 2] << 8) | hash[desplazamiento + 3];
        return (valor % 1_000_000).ToString("D6");
    }

    /// <returns>El paso del código si es válido y posterior a <paramref name="ultimoUsado"/>; si no, null.</returns>
    public static long? Verificar(byte[] secreto, string? codigo, DateTimeOffset ahora, long? ultimoUsado)
    {
        codigo = codigo?.Replace(" ", "").Trim();
        if (codigo is not { Length: Digitos } || !codigo.All(char.IsAsciiDigit))
            return null;
        var actual = Paso(ahora);
        for (var p = actual - 1; p <= actual + 1; p++)
        {
            if (ultimoUsado is { } u && p <= u)
                continue; // un código ya usado no vuelve a servir
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Codigo(secreto, p)), Encoding.ASCII.GetBytes(codigo)))
                return p;
        }

        return null;
    }

    public static string Base32(byte[] datos)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in datos)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alfabeto[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            sb.Append(Alfabeto[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    /// <summary>Enlace otpauth:// para agregar la cuenta en la aplicación autenticadora.</summary>
    public static string Uri(string emisor, string cuenta, byte[] secreto) =>
        $"otpauth://totp/{System.Uri.EscapeDataString(emisor)}:{System.Uri.EscapeDataString(cuenta)}?secret={Base32(secreto)}&issuer={System.Uri.EscapeDataString(emisor)}&digits={Digitos}&period={SegundosPorPaso}";
}
