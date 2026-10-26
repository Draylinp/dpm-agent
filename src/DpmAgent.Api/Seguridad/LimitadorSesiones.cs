using System.Threading.RateLimiting;
using DpmAgent.Api.Agent;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Seguridad;

public sealed class LimiteExcedidoException()
    : EntradaRechazadaException("entrada.limite_mensajes", StatusCodes.Status429TooManyRequests);

/// <summary>
/// Límite de mensajes por conversación, aplicado en el agente: cubre el chat web,
/// las notas de voz, las fotos y WhatsApp por igual.
/// </summary>
public sealed class LimitadorSesiones : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limitador;

    public LimitadorSesiones(IOptions<LimitesOptions> opciones)
    {
        var porMinuto = Math.Max(1, opciones.Value.MensajesPorMinutoPorSesion);
        _limitador = PartitionedRateLimiter.Create<string, string>(sesion => RateLimitPartition.GetSlidingWindowLimiter(sesion, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = porMinuto,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }));
    }

    /// <exception cref="LimiteExcedidoException">Si la conversación pasó del límite.</exception>
    public void Verificar(string sesionId)
    {
        using var permiso = _limitador.AttemptAcquire(sesionId);
        if (!permiso.IsAcquired)
            throw new LimiteExcedidoException();
    }

    public void Dispose() => _limitador.Dispose();
}
