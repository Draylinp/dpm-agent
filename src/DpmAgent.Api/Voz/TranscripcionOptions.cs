namespace DpmAgent.Api.Voz;

/// <summary>
/// Configuración de las notas de voz (sección "Transcripcion").
/// Usa la misma URL base y clave que <see cref="Model.MetaOptions"/>.
/// </summary>
public sealed class TranscripcionOptions
{
    public const string SectionName = "Transcripcion";

    public string Modelo { get; set; } = "muse-voice-transcribe-1.0";

    /// <summary>Ruta relativa a Meta:BaseUrl.</summary>
    public string Ruta { get; set; } = "audio/transcriptions";

    /// <summary>Idioma esperado (ISO 639-1). Vacío para que el servicio lo detecte.</summary>
    public string? Idioma { get; set; } = "es";

    /// <summary>Tamaño máximo del audio. WhatsApp manda Opus a ~16 kbps: 2 MB son varios minutos.</summary>
    public long MaxBytes { get; set; } = 2 * 1024 * 1024;

    public double MaxDuracionSegundos { get; set; } = 120;

    public int TimeoutSeconds { get; set; } = 60;
}
