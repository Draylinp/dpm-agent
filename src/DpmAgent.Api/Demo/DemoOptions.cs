namespace DpmAgent.Api.Demo;

/// <summary>Demo pública para los jueces (sección "Demo").</summary>
public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Habilitado { get; set; }

    /// <summary>Mensajes por día y por IP en el chat público (protege los créditos del modelo).</summary>
    public int MensajesPorDiaPorIp { get; set; } = 40;

    /// <summary>Reinicios por hora y por IP.</summary>
    public int ReiniciosPorHoraPorIp { get; set; } = 10;

    /// <summary>Si se define, se crea el usuario "demo" del panel con esta clave y se muestra en la página.</summary>
    public string? ClavePanel { get; set; }
}
