using System.Globalization;

namespace DpmAgent.Api.Dpm;

/// <summary>
/// Formato legible de los ids que ve el modelo (PAC-001, MED-01, CITA-12, ESC-3)
/// y su conversión a la clave numérica de la base de datos.
/// </summary>
public static class IdsDpm
{
    public static string Paciente(int id) => $"PAC-{id:000}";

    public static string Medico(int id) => $"MED-{id:00}";

    public static string Cita(int id) => $"CITA-{id}";

    public static string Escalamiento(int id) => $"ESC-{id}";

    public static int? Leer(string? valor, string prefijo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return null;

        var texto = valor.Trim();
        if (!texto.StartsWith(prefijo + "-", StringComparison.OrdinalIgnoreCase))
            return null;

        return int.TryParse(texto[(prefijo.Length + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : null;
    }
}
