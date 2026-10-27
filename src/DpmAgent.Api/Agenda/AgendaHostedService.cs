using DpmAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Agenda;

/// <summary>
/// Corre el motor de agenda de cada consultorio y luego despacha los mensajes salientes.
/// Se ejecuta cada <see cref="AgendaOptions.IntervaloSegundos"/> o antes si alguien lo despierta.
/// </summary>
public sealed class AgendaHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly DespertadorAgenda _despertador;
    private readonly AgendaOptions _opciones;
    private readonly ILogger<AgendaHostedService> _logger;

    public AgendaHostedService(
        IServiceScopeFactory scopes,
        DespertadorAgenda despertador,
        IOptions<AgendaOptions> opciones,
        ILogger<AgendaHostedService> logger)
    {
        _scopes = scopes;
        _despertador = despertador;
        _opciones = opciones.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opciones.MotorActivo)
            return;

        var intervalo = TimeSpan.FromSeconds(Math.Max(1, _opciones.IntervaloSegundos));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await VueltaAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError("Falló una vuelta del motor de agenda; se reintenta en la siguiente: {Error}", Seguridad.Privacidad.Excepcion(ex));
            }

            try
            {
                await _despertador.EsperarAsync(intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Una vuelta completa: todos los consultorios y luego el despacho.</summary>
    public async Task VueltaAsync(CancellationToken ct)
    {
        List<int> consultorios;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DpmDbContext>();
            consultorios = await db.Consultorios.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        }

        foreach (var id in consultorios)
        {
            await using var scope = _scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ConsultorioDelAmbito>().Establecer(id);
            var r = await scope.ServiceProvider.GetRequiredService<MotorAgenda>().ProcesarAsync(ct);
            if (r.OfertasEnviadas + r.OfertasVencidas + r.HuecosCerrados + r.Recordatorios > 0)
                _logger.LogInformation("Agenda consultorio {Id}: {Resultado}", id, r);
        }

        await using (var scope = _scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<DespachadorMensajes>().DespacharPendientesAsync(ct);
    }
}
