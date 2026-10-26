using DpmAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Seguridad;

/// <summary>Cuánto se guardan los datos (sección "Retencion"). 0 desactiva el borrado de ese tipo.</summary>
public sealed class RetencionOptions
{
    public const string SectionName = "Retencion";

    /// <summary>Conversaciones y sus mensajes, contados desde el último mensaje.</summary>
    public int DiasConversaciones { get; set; } = 30;

    /// <summary>Mensajes de WhatsApp recibidos y enviados ya procesados.</summary>
    public int DiasMensajesCanal { get; set; } = 30;

    /// <summary>Carnets confirmados o descartados (los datos útiles ya pasaron al paciente).</summary>
    public int DiasCarnetsResueltos { get; set; } = 30;

    /// <summary>La bitácora se guarda más tiempo: es el registro de auditoría.</summary>
    public int DiasBitacora { get; set; } = 365;

    public int IntervaloHoras { get; set; } = 6;
}

public sealed record ResultadoRetencion(int Conversaciones, int MensajesCanal, int Carnets, int Bitacora);

/// <summary>Borra lo que pasó su tiempo de retención, en todos los consultorios.</summary>
public sealed class ServicioRetencion(DpmDbContext db, IOptions<RetencionOptions> opciones, TimeProvider clock)
{
    public async Task<ResultadoRetencion> LimpiarAsync(CancellationToken ct)
    {
        var o = opciones.Value;
        var ahora = clock.GetUtcNow().UtcDateTime;

        var conversaciones = 0;
        if (o.DiasConversaciones > 0)
        {
            var limite = ahora.AddDays(-o.DiasConversaciones);
            var viejas = db.Conversaciones.IgnoreQueryFilters().Where(c => c.ActualizadaUtc < limite).Select(c => c.Id);
            await db.Mensajes.Where(m => viejas.Contains(m.ConversacionId)).ExecuteDeleteAsync(ct);
            conversaciones = await db.Conversaciones.IgnoreQueryFilters().Where(c => c.ActualizadaUtc < limite).ExecuteDeleteAsync(ct);
        }

        var mensajesCanal = 0;
        if (o.DiasMensajesCanal > 0)
        {
            var limite = ahora.AddDays(-o.DiasMensajesCanal);
            mensajesCanal += await db.MensajesEntrantes.IgnoreQueryFilters()
                .Where(m => m.Estado != "pendiente" && m.RecibidoUtc < limite).ExecuteDeleteAsync(ct);
            mensajesCanal += await db.MensajesSalientes.IgnoreQueryFilters()
                .Where(m => m.Estado != "pendiente" && m.CreadoUtc < limite).ExecuteDeleteAsync(ct);
        }

        var carnets = 0;
        if (o.DiasCarnetsResueltos > 0)
        {
            var limite = ahora.AddDays(-o.DiasCarnetsResueltos);
            carnets = await db.CarnetsPendientes.IgnoreQueryFilters()
                .Where(c => c.Estado != "pendiente" && c.ResueltoUtc < limite).ExecuteDeleteAsync(ct);
        }

        var bitacora = 0;
        if (o.DiasBitacora > 0)
        {
            var limite = ahora.AddDays(-o.DiasBitacora);
            bitacora = await db.Bitacora.IgnoreQueryFilters().Where(b => b.FechaUtc < limite).ExecuteDeleteAsync(ct);
        }

        return new ResultadoRetencion(conversaciones, mensajesCanal, carnets, bitacora);
    }
}

public sealed class RetencionHostedService(
    IServiceScopeFactory scopes, IOptions<RetencionOptions> opciones, ILogger<RetencionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromHours(Math.Max(1, opciones.Value.IntervaloHoras));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var r = await scope.ServiceProvider.GetRequiredService<ServicioRetencion>().LimpiarAsync(stoppingToken);
                if (r.Conversaciones + r.MensajesCanal + r.Carnets + r.Bitacora > 0)
                    logger.LogInformation("Retención: se borraron {Resultado}", r);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falló la limpieza por retención; se reintenta en el siguiente intervalo.");
            }

            try
            {
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
