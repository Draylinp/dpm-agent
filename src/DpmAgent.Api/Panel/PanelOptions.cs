namespace DpmAgent.Api.Panel;

/// <summary>Configuración del panel de la secretaria (sección "Panel").</summary>
public sealed class PanelOptions
{
    public const string SectionName = "Panel";

    /// <summary>Usuario administrador que se crea si el consultorio no tiene ninguno.</summary>
    public string UsuarioInicial { get; set; } = "secretaria";

    public string NombreInicial { get; set; } = "Secretaria (ficticia)";

    /// <summary>
    /// Clave del usuario inicial (variable Panel__ClaveInicial). En desarrollo, si falta, se genera
    /// una al azar y se muestra una sola vez en la consola. En producción es obligatoria.
    /// </summary>
    public string? ClaveInicial { get; set; }

    /// <summary>La sesión del panel se cierra tras estos minutos sin actividad.</summary>
    public int MinutosInactividad { get; set; } = 30;

    /// <summary>Duración máxima de una sesión aunque haya actividad; después hay que volver a entrar.</summary>
    public int HorasMaximasSesion { get; set; } = 8;

    public int IntentosAntesDeBloquear { get; set; } = 5;

    public int MinutosBloqueo { get; set; } = 15;
}
