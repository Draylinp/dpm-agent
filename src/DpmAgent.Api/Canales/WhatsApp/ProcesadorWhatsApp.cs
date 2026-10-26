using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Data;
using DpmAgent.Api.Voz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Canales.WhatsApp;

/// <summary>
/// Pasa al agente los mensajes de WhatsApp guardados por el webhook, en orden de llegada,
/// y encola la respuesta. Texto → agente; nota de voz → transcripción → agente;
/// foto → lectura de carnet → agente.
/// </summary>
public sealed class ProcesadorWhatsApp
{
    public const string SoloTextoVozYCarnet =
        "Por ahora solo puedo leer mensajes de texto, notas de voz y fotos del carnet de la ARS.";

    public const string Disculpa =
        "Disculpe, tuvimos un problema para procesar su mensaje. Ya avisamos al personal del consultorio.";

    private const int Lote = 20;
    private const int MaxCaracteres = 2000;

    private readonly IServiceScopeFactory _scopes;
    private readonly WhatsAppOptions _opciones;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProcesadorWhatsApp> _logger;

    public ProcesadorWhatsApp(
        IServiceScopeFactory scopes, IOptions<WhatsAppOptions> opciones, TimeProvider clock, ILogger<ProcesadorWhatsApp> logger)
    {
        _scopes = scopes;
        _opciones = opciones.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <returns>Cuántos mensajes se procesaron (con éxito o con fallo definitivo).</returns>
    public async Task<int> ProcesarPendientesAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DpmDbContext>();
        var pendientes = await db.MensajesEntrantes.IgnoreQueryFilters()
            .Where(m => m.Estado == "pendiente")
            .OrderBy(m => m.Id)
            .Take(Lote)
            .ToListAsync(ct);

        var procesados = 0;
        foreach (var m in pendientes)
        {
            var sesionId = CanalesConocidos.SesionWhatsApp(m.Telefono);
            try
            {
                var respuesta = await ResponderAsync(m, sesionId, ct);
                await EncolarAsync(m.ConsultorioId, sesionId, m.Telefono, respuesta, ct);
                m.Estado = "procesado";
                m.ProcesadoUtc = _clock.GetUtcNow().UtcDateTime;
                m.Error = null;
                procesados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m.Intentos++;
                m.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                _logger.LogWarning(ex, "No se pudo procesar el mensaje de WhatsApp {Id} (intento {Intento})", m.Id, m.Intentos);

                if (m.Intentos >= _opciones.MaxIntentos)
                {
                    m.Estado = "fallido";
                    procesados++;
                    await AvisarFalloAsync(m.ConsultorioId, sesionId, m.Telefono, ct);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        return procesados;
    }

    private async Task<string> ResponderAsync(Data.Entidades.MensajeEntrante m, string sesionId, CancellationToken ct)
    {
        await using var scope = Ambito(m.ConsultorioId);
        var sp = scope.ServiceProvider;

        try
        {
            switch (m.Tipo)
            {
                case "text":
                    if (string.IsNullOrWhiteSpace(m.Texto))
                        return SoloTextoVozYCarnet;
                    if (m.Texto.Length > MaxCaracteres)
                        return "Su mensaje es muy largo. ¿Puede enviarlo más corto?";
                    return (await sp.GetRequiredService<ReceptionAgent>().RunAsync(sesionId, m.Texto, m.Telefono, ct)).Respuesta;

                case "audio":
                {
                    var voz = sp.GetRequiredService<ProcesadorNotaVoz>();
                    var audio = await sp.GetRequiredService<IWhatsAppCloudApi>().DescargarMediaAsync(m.MediaId!, voz.Limites.MaxBytes, ct);
                    return (await voz.ProcesarAsync(audio.Datos, sesionId, m.Telefono, ct)).Respuesta;
                }

                case "image":
                {
                    var carnet = sp.GetRequiredService<ProcesadorCarnet>();
                    var imagen = await sp.GetRequiredService<IWhatsAppCloudApi>().DescargarMediaAsync(m.MediaId!, carnet.MaxBytes, ct);
                    return (await carnet.ProcesarAsync(imagen.Datos, sesionId, m.Telefono, ct)).Respuesta;
                }

                default:
                    return SoloTextoVozYCarnet;
            }
        }
        catch (EntradaRechazadaException ex)
        {
            // Audio muy largo, foto ilegible, etc.: se le explica al paciente.
            return ex.Message;
        }
    }

    private async Task EncolarAsync(int consultorioId, string sesionId, string telefono, string texto, CancellationToken ct)
    {
        await using var scope = Ambito(consultorioId);
        await scope.ServiceProvider.GetRequiredService<Notificador>().EncolarRespuestaAsync(sesionId, telefono, texto, ct);
    }

    private async Task AvisarFalloAsync(int consultorioId, string sesionId, string telefono, CancellationToken ct)
    {
        try
        {
            await using var scope = Ambito(consultorioId);
            var sp = scope.ServiceProvider;
            await sp.GetRequiredService<Dpm.IDpmGateway>().RegistrarEscalamientoAsync(
                sesionId, null, "No se pudo procesar un mensaje de WhatsApp después de varios intentos.", false, ct);
            await sp.GetRequiredService<Notificador>().EncolarRespuestaAsync(sesionId, telefono, Disculpa, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Tampoco se pudo avisar del fallo en la sesión {Sesion}", sesionId);
        }
    }

    private AsyncServiceScope Ambito(int consultorioId)
    {
        var scope = _scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ConsultorioDelAmbito>().Establecer(consultorioId);
        return scope;
    }
}

/// <summary>Corre <see cref="ProcesadorWhatsApp"/> cuando llega un mensaje (o cada pocos segundos).</summary>
public sealed class ProcesadorWhatsAppHostedService(
    ProcesadorWhatsApp procesador,
    DespertadorEntrantes despertador,
    IOptions<WhatsAppOptions> opciones,
    ILogger<ProcesadorWhatsAppHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!opciones.Value.ProcesadorActivo)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await procesador.ProcesarPendientesAsync(stoppingToken) > 0)
                {
                    // Sigue mientras haya lotes completos por procesar.
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falló el procesador de WhatsApp; se reintenta.");
            }

            try
            {
                await despertador.EsperarAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

public static class WhatsAppExtensions
{
    public static IServiceCollection AddWhatsApp(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<WhatsAppOptions>(config.GetSection(WhatsAppOptions.SectionName));
        services.AddHttpClient<IWhatsAppCloudApi, WhatsAppCloudApi>();
        services.AddScoped<IMessageChannel, WhatsAppCloudChannel>();
        services.AddSingleton<DespertadorEntrantes>();
        services.AddSingleton<ProcesadorWhatsApp>();
        services.AddHostedService<ProcesadorWhatsAppHostedService>();
        return services;
    }
}
