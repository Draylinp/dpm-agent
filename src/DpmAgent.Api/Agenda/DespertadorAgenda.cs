namespace DpmAgent.Api.Agenda;

/// <summary>
/// Señal para que la tarea de agenda no espere al siguiente intervalo
/// (por ejemplo, justo después de una cancelación o de encolar un mensaje).
/// </summary>
public sealed class DespertadorAgenda
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
