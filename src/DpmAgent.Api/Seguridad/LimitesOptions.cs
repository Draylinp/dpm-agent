namespace DpmAgent.Api.Seguridad;

/// <summary>Límites de uso (sección "Limites").</summary>
public sealed class LimitesOptions
{
    public const string SectionName = "Limites";

    /// <summary>Mensajes por minuto en una misma conversación, por cualquier canal.</summary>
    public int MensajesPorMinutoPorSesion { get; set; } = 12;

    /// <summary>Peticiones por minuto desde una misma IP a los endpoints públicos (chat, voz, carnet).</summary>
    public int PeticionesPorMinutoPorIp { get; set; } = 120;

    /// <summary>Intentos de inicio de sesión por minuto desde una misma IP (además del bloqueo por cuenta).</summary>
    public int LoginPorMinutoPorIp { get; set; } = 10;

    /// <summary>
    /// Confiar en X-Forwarded-For / X-Forwarded-Proto. Solo con un proxy delante que los fije
    /// (Azure App Service, contenedor detrás de un balanceador); si no, cualquiera falsearía su IP.
    /// </summary>
    public bool ConfiarEncabezadosReenviados { get; set; }
}
