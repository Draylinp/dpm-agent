namespace DpmAgent.Api.Model;

/// <summary>
/// Uso de una llamada al modelo: solo cifras, nunca el contenido de los mensajes.
/// </summary>
/// <param name="Proposito">agente, filtro, juez... (ver <see cref="ContextoUso"/>).</param>
/// <param name="ConsultorioId">Consultorio y conversación de la llamada, si se conocen (ver <see cref="ContextoUso"/>).</param>
public sealed record UsoModelo(
    string Proveedor, string Modelo, string Proposito, int TokensEntrada, int TokensSalida, int Milisegundos, DateTimeOffset Fecha,
    int? ConsultorioId = null, string? SesionId = null);

/// <summary>Recibe el uso de cada llamada al modelo (costos y métricas de evaluación).</summary>
public interface IRegistroUso
{
    void Registrar(UsoModelo uso);
}

/// <summary>
/// Para qué se llama al modelo en este momento y desde qué conversación. Lo fija quien llama
/// (el agente, el filtro de salida, el juez de la evaluación) y lo lee el cliente al registrar el uso.
/// </summary>
public static class ContextoUso
{
    private sealed record Estado(string? Proposito, int? ConsultorioId, string? SesionId);

    private static readonly AsyncLocal<Estado?> Actual = new();

    public static string Proposito => Actual.Value?.Proposito ?? "agente";

    public static int? ConsultorioId => Actual.Value?.ConsultorioId;

    public static string? SesionId => Actual.Value?.SesionId;

    public static IDisposable Usar(string proposito) =>
        Cambiar(new Estado(proposito, Actual.Value?.ConsultorioId, Actual.Value?.SesionId));

    /// <summary>Las llamadas que siguen pertenecen a esta conversación (hasta el Dispose).</summary>
    public static IDisposable Conversacion(int? consultorioId, string sesionId) =>
        Cambiar(new Estado(Actual.Value?.Proposito, consultorioId, sesionId));

    /// <summary>Uso de una llamada con el propósito y la conversación actuales.</summary>
    public static UsoModelo Uso(string proveedor, string modelo, int entrada, int salida, int milisegundos) =>
        new(proveedor, modelo, Proposito, entrada, salida, milisegundos, DateTimeOffset.UtcNow, ConsultorioId,
            SesionId is null ? null : Seguridad.Privacidad.Seudonimo(SesionId));

    private static IDisposable Cambiar(Estado nuevo)
    {
        var anterior = Actual.Value;
        Actual.Value = nuevo;
        return new Restaurar(anterior);
    }

    private sealed class Restaurar(Estado? anterior) : IDisposable
    {
        public void Dispose() => Actual.Value = anterior;
    }
}

/// <summary>Acumula el uso en memoria (evaluación y pruebas).</summary>
public sealed class RegistroUsoEnMemoria : IRegistroUso
{
    private readonly List<UsoModelo> _usos = [];

    public IReadOnlyList<UsoModelo> Usos
    {
        get
        {
            lock (_usos)
                return _usos.ToList();
        }
    }

    public void Registrar(UsoModelo uso)
    {
        lock (_usos)
            _usos.Add(uso);
    }
}
