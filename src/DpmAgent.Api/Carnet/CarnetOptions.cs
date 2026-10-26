namespace DpmAgent.Api.Carnet;

/// <summary>Configuración de la lectura de carnets (sección "Carnet").</summary>
public sealed class CarnetOptions
{
    public const string SectionName = "Carnet";

    /// <summary>Modelo con entrada de imagen y salida estructurada. Vacío: el mismo de Meta:Model.</summary>
    public string? Modelo { get; set; }

    /// <summary>Las fotos de WhatsApp pesan unos cientos de KB; 4 MB deja margen sin inflar la petición.</summary>
    public long MaxBytes { get; set; } = 4 * 1024 * 1024;

    public int TimeoutSeconds { get; set; } = 60;
}
