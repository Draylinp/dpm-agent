namespace DpmAgent.Api.Voz;

/// <param name="Texto">Lo que dijo el paciente.</param>
/// <param name="DuracionSegundos">Duración que informa el servicio, si la informa.</param>
public sealed record Transcripcion(string Texto, double? DuracionSegundos);

/// <summary>Convierte una nota de voz en texto. El audio solo vive en memoria durante la llamada.</summary>
public interface ITranscriber
{
    Task<Transcripcion> TranscribirAsync(AudioRecibido audio, CancellationToken ct);
}
