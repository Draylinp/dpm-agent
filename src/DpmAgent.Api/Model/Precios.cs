using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Model;

/// <summary>
/// Precios por millón de tokens, por modelo (sección "Precios"). No van fijos en el código:
/// cambian por proveedor y con el tiempo. Un modelo sin precio da costo desconocido, no cero.
/// </summary>
public sealed class PreciosOptions
{
    public const string SectionName = "Precios";

    public string Moneda { get; set; } = "USD";

    /// <summary>Por nombre de modelo, por ejemplo "muse-spark-1.2".</summary>
    public Dictionary<string, PrecioModelo> Modelos { get; set; } = [];
}

public sealed class PrecioModelo
{
    public decimal EntradaPorMillon { get; set; }

    public decimal SalidaPorMillon { get; set; }
}

public sealed class CalculadoraCosto
{
    private readonly PreciosOptions _precios;

    public CalculadoraCosto(IOptions<PreciosOptions> precios) => _precios = precios.Value;

    public string Moneda => _precios.Moneda;

    public bool TienePrecio(string modelo) => _precios.Modelos.ContainsKey(modelo);

    /// <returns>Costo de la llamada, o null si el modelo no tiene precio configurado.</returns>
    public decimal? Costo(string modelo, long tokensEntrada, long tokensSalida) =>
        _precios.Modelos.TryGetValue(modelo, out var p)
            ? (tokensEntrada * p.EntradaPorMillon + tokensSalida * p.SalidaPorMillon) / 1_000_000m
            : null;
}
