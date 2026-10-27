using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DpmAgent.Api.Cumplimiento;

/// <summary>
/// Pedidos que el servidor atiende sin pasar por el modelo, para que siempre funcionen:
/// darse de baja de los avisos (STOP) y hablar con una persona, en es/en/ht (y fr/pt).
/// </summary>
public static partial class PedidosDirectos
{
    /// <summary>Palabras de baja: el mensaje completo debe ser una de ellas (como en los avisos: "responda STOP").</summary>
    private static readonly HashSet<string> PalabrasBaja =
    [
        "stop", "baja", "darme de baja", "dar de baja", "no mas avisos", "no mas mensajes",
        "unsubscribe", "stop messages", "no more messages",
        "sispann", "rete", "pa voye m mesaj anko",
        "arret", "arreter", "parar", "sair",
    ];

    public static bool EsBaja(string? mensaje)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
            return false;
        var limpio = SinAcentos(mensaje).Trim().TrimEnd('.', '!', ' ').ToLowerInvariant();
        return PalabrasBaja.Contains(limpio);
    }

    public static bool EsPedidoDePersona(string? mensaje) =>
        !string.IsNullOrWhiteSpace(mensaje) && Persona().IsMatch(SinAcentos(mensaje));

    [GeneratedRegex(
        @"\b(hablar|comunicar(me)?|chatear)\s+con\s+(una?\s+|la\s+|el\s+)?(persona|humano|ser humano|alguien|secretaria|recepcionista|agente real|persona real)\b" +
        @"|\b(quiero|necesito|prefiero)\s+(una?\s+)?(persona|humano)\b" +
        @"|\b(talk|speak|chat)\s+(to|with)\s+(a\s+|an\s+|the\s+)?(real\s+)?(person|human|someone|somebody|agent|staff|receptionist|secretary)\b" +
        @"|\b(real|live)\s+(person|human|agent)\b" +
        @"|\bpale\s+(ak|avek)\s+(yon\s+)?(moun|sekrete|resepsyonis)\b|\bmwen\s+vle\s+yon\s+moun\b" +
        @"|\bparler\s+(a|avec)\s+(une?\s+)?(personne|humain|secretaire)\b" +
        @"|\bfalar\s+com\s+(uma?\s+)?(pessoa|humano|atendente|secretaria)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Persona();

    private static string SinAcentos(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
