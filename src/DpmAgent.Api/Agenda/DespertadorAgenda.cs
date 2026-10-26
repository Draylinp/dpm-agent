namespace DpmAgent.Api.Agenda;

/// <summary>
/// Señal para que una tarea en segundo plano no espere al siguiente intervalo.
/// Varios avisos seguidos cuentan como uno.
/// </summary>
public class Despertador
{
    private readonly SemaphoreSlim _senal = new(0, 1);

    public void Despertar()
    {
        try
        {
            _senal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Ya había un aviso pendiente.
        }
    }

    public Task<bool> EsperarAsync(TimeSpan maximo, CancellationToken ct) => _senal.WaitAsync(maximo, ct);
}

/// <summary>Despierta al motor de agenda (cancelaciones, mensajes por enviar).</summary>
public sealed class DespertadorAgenda : Despertador;

/// <summary>Despierta al procesador de mensajes entrantes de WhatsApp.</summary>
public sealed class DespertadorEntrantes : Despertador;
