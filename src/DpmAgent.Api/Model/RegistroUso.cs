namespace DpmAgent.Api.Model;

/// <summary>
/// Uso de una llamada al modelo: solo cifras, nunca el contenido de los mensajes.
/// </summary>
/// <param name="Proposito">agente, filtro, juez... (ver <see cref="ContextoUso"/>).</param>
public sealed record UsoModelo(
    string Proveedor, string Modelo, string Proposito, int TokensEntrada, int TokensSalida, int Milisegundos, DateTimeOffset Fecha);

/// <summary>Recibe el uso de cada llamada al modelo (costos y métricas de evaluación).</summary>
public interface IRegistroUso
{
    void Registrar(UsoModelo uso);
}

/// <summary>
/// Para qué se llama al modelo en este momento. Lo fija quien llama (el agente, el filtro de salida,
/// el juez de la evaluación) y lo lee el cliente al registrar el uso.
/// </summary>
public static class ContextoUso
{
    private static readonly AsyncLocal<string?> Actual = new();

    public static string Proposito => Actual.Value ?? "agente";

    public static IDisposable Usar(string proposito)
    {
        var anterior = Actual.Value;
        Actual.Value = proposito;
        return new Restaurar(anterior);
    }

    private sealed class Restaurar(string? anterior) : IDisposable
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
