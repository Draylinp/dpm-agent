using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Model;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Costos;

public sealed record CostoModelo(string Modelo, int Llamadas, long TokensEntrada, long TokensSalida, decimal? Costo);

/// <param name="CostoTotal">Suma de los modelos con precio; los que no tienen van en <paramref name="ModelosSinPrecio"/>.</param>
public sealed record ResumenCostos(
    int Dias,
    string Moneda,
    int Conversaciones,
    int CitasAgendadas,
    int Llamadas,
    long TokensEntrada,
    long TokensSalida,
    decimal? CostoTotal,
    decimal? CostoPorConversacion,
    decimal? CostoPorCita,
    IReadOnlyList<string> ModelosSinPrecio,
    IReadOnlyList<CostoModelo> PorModelo);

/// <summary>Costo del modelo en el consultorio: total, por conversación y por cita agendada por el agente.</summary>
public sealed class ServicioCostos(DpmDbContext db, CalculadoraCosto calculadora, TimeProvider clock)
{
    /// <summary>Acciones de la bitácora que cuentan como una cita agendada por el agente.</summary>
    private static readonly string[] AccionesCita = ["cita_creada", "oferta_aceptada"];

    public async Task<ResumenCostos> ResumenAsync(int dias, CancellationToken ct)
    {
        dias = Math.Clamp(dias, 1, 365);
        var desde = clock.GetUtcNow().UtcDateTime.AddDays(-dias);

        var porModelo = await db.UsosModelo.AsNoTracking()
            .Where(u => u.FechaUtc >= desde)
            .GroupBy(u => u.Modelo)
            .Select(g => new { Modelo = g.Key, Llamadas = g.Count(), Entrada = g.Sum(u => (long)u.TokensEntrada), Salida = g.Sum(u => (long)u.TokensSalida) })
            .ToListAsync(ct);

        var conversaciones = await db.UsosModelo.AsNoTracking()
            .Where(u => u.FechaUtc >= desde && u.SesionId != null)
            .Select(u => u.SesionId)
            .Distinct()
            .CountAsync(ct);

        var citas = await db.Bitacora.AsNoTracking()
            .CountAsync(b => b.FechaUtc >= desde && b.Actor == Bitacora.Agente && AccionesCita.Contains(b.Accion), ct);

        var modelos = porModelo
            .OrderBy(m => m.Modelo, StringComparer.Ordinal)
            .Select(m => new CostoModelo(m.Modelo, m.Llamadas, m.Entrada, m.Salida, calculadora.Costo(m.Modelo, m.Entrada, m.Salida)))
            .ToList();

        var sinPrecio = modelos.Where(m => m.Costo is null).Select(m => m.Modelo).ToList();
        decimal? total = modelos.Count == sinPrecio.Count ? null : modelos.Sum(m => m.Costo ?? 0);

        return new ResumenCostos(
            dias,
            calculadora.Moneda,
            conversaciones,
            citas,
            modelos.Sum(m => m.Llamadas),
            modelos.Sum(m => m.TokensEntrada),
            modelos.Sum(m => m.TokensSalida),
            total,
            total is { } t && conversaciones > 0 ? Math.Round(t / conversaciones, 6) : null,
            total is { } t2 && citas > 0 ? Math.Round(t2 / citas, 6) : null,
            sinPrecio,
            modelos);
    }
}
