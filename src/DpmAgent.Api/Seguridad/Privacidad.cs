using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DpmAgent.Api.Seguridad;

/// <summary>
/// Para que ningún log, informe ni mensaje de error lleve datos de pacientes: los ids de sesión de WhatsApp
/// contienen el número ("wa-8095550101"), así que fuera de la base se usa un seudónimo estable.
/// </summary>
public static partial class Privacidad
{
    /// <summary>Seudónimo estable de una sesión (el mismo id da siempre el mismo valor; no se puede revertir).</summary>
    public static string Seudonimo(string? sesionId)
    {
        if (string.IsNullOrEmpty(sesionId))
            return "-";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sesionId));
        return "s-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }

    /// <summary>Quita números de teléfono, cédulas, correos y afiliados de un texto antes de escribirlo en un log.</summary>
    public static string Enmascarar(string? texto, int maximo = 300)
    {
        if (string.IsNullOrEmpty(texto))
            return "";
        var limpio = Correo().Replace(Numeros().Replace(texto, "[número]"), "[correo]");
        return limpio.Length <= maximo ? limpio : limpio[..maximo] + "…";
    }

    /// <summary>
    /// Una excepción para el log: tipo, mensaje y pila, enmascarados. Las excepciones de servicios externos
    /// pueden traer números o texto del paciente en el mensaje; nunca se registran en crudo.
    /// </summary>
    public static string Excepcion(Exception ex) => Enmascarar(ex.ToString(), 4000);

    // 7 o más dígitos, con o sin separadores (+1 809-555-0101, 8095550101, 001-1234567-8).
    [GeneratedRegex(@"\+?\d(?:[\s\-().]*\d){6,}")]
    private static partial Regex Numeros();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.]+")]
    private static partial Regex Correo();
}

/// <summary>Registra los errores no controlados sin datos personales y deja que ProblemDetails responda.</summary>
public sealed class ManejadorErroresPrivado(ILogger<ManejadorErroresPrivado> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        logger.LogError("Error no controlado en {Metodo} {Ruta}: {Error}", http.Request.Method, http.Request.Path.Value, Privacidad.Excepcion(ex));
        return ValueTask.FromResult(false);
    }
}
