using System.Threading.Channels;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Model;

namespace DpmAgent.Api.Costos;

/// <summary>
/// Recibe el uso de cada llamada al modelo y lo guarda en segundo plano, para no sumar
/// una escritura a la base en el camino de la respuesta. Solo cifras: nunca el contenido.
/// </summary>
public sealed class ColaUso : IRegistroUso
{
    // Si la base se cae, se pierden los registros más viejos antes que llenar la memoria.
    private readonly Channel<UsoModelo> _cola = Channel.CreateBounded<UsoModelo>(
        new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Registrar(UsoModelo uso) => _cola.Writer.TryWrite(uso);

    public ValueTask<bool> EsperarAsync(CancellationToken ct) => _cola.Reader.WaitToReadAsync(ct);

    /// <summary>Guarda lo pendiente, agrupado por consultorio (cada uno con su ámbito).</summary>
    public async Task<int> GuardarPendientesAsync(IServiceScopeFactory scopes, int consultorioPredeterminado, CancellationToken ct)
    {
        var pendientes = new List<UsoModelo>();
        while (pendientes.Count < 500 && _cola.Reader.TryRead(out var uso))
            pendientes.Add(uso);

        foreach (var grupo in pendientes.GroupBy(u => u.ConsultorioId ?? consultorioPredeterminado))
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ConsultorioDelAmbito>().Establecer(grupo.Key);
            var db = scope.ServiceProvider.GetRequiredService<DpmDbContext>();
            db.UsosModelo.AddRange(grupo.Select(u => new RegistroUsoModelo
            {
                ConsultorioId = grupo.Key,
                FechaUtc = u.Fecha.UtcDateTime,
                Proveedor = Recortar(u.Proveedor, 40),
                Modelo = Recortar(u.Modelo, 120),
                Proposito = Recortar(u.Proposito, 20),
                SesionId = u.SesionId is null ? null : Recortar(u.SesionId, 100),
                TokensEntrada = u.TokensEntrada,
                TokensSalida = u.TokensSalida,
                Milisegundos = u.Milisegundos,
            }));
            await db.SaveChangesAsync(ct);
        }

        return pendientes.Count;
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}

public sealed class GuardadoUsoHostedService(
    ColaUso cola, IServiceScopeFactory scopes, BaseDatosOptions baseDatos, ILogger<GuardadoUsoHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await cola.EsperarAsync(stoppingToken))
            {
                try
                {
                    await cola.GuardarPendientesAsync(scopes, baseDatos.ConsultorioId, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError("No se pudo guardar el uso del modelo; esas cifras se pierden: {Error}", Seguridad.Privacidad.Excepcion(ex));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
