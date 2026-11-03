using DpmAgent.Api.Agent;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Voz;

/// <summary>
/// Nota de voz → validación → Muse Voice Transcribe → agente. La usan el endpoint HTTP y WhatsApp.
/// El audio solo vive en memoria.
/// </summary>
public sealed class ProcesadorNotaVoz
{
    /// <summary>Prefijo con el que el agente reconoce que el mensaje viene de una transcripción automática.</summary>
    public const string PrefijoNotaDeVoz = "[Nota de voz] ";

    private const int MaxCaracteres = 2000;

    private readonly ITranscriber _transcriber;
    private readonly ReceptionAgent _agent;
    private readonly TranscripcionOptions _limites;

    public ProcesadorNotaVoz(ITranscriber transcriber, ReceptionAgent agent, IOptions<TranscripcionOptions> opciones)
    {
        _transcriber = transcriber;
        _agent = agent;
        _limites = opciones.Value;
    }

    public TranscripcionOptions Limites => _limites;

    /// <exception cref="EntradaRechazadaException">Audio inválido, demasiado largo o sin texto entendible.</exception>
    public async Task<AudioChatReply> ProcesarAsync(byte[] datos, string? sesionId, string? telefono, CancellationToken ct)
    {
        var audio = AudioRecibido.Validar(datos, _limites);
        var transcripcion = await _transcriber.TranscribirAsync(audio, ct);

        if (transcripcion.DuracionSegundos > _limites.MaxDuracionSegundos)
            throw new EntradaRechazadaException("entrada.audio_largo", StatusCodes.Status400BadRequest,
                _limites.MaxDuracionSegundos.ToString("0", System.Globalization.CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(transcripcion.Texto))
        {
            throw new EntradaRechazadaException("entrada.audio_no_entendido", StatusCodes.Status422UnprocessableEntity);
        }

        if (transcripcion.Texto.Length > MaxCaracteres)
            throw new EntradaRechazadaException("entrada.audio_texto_largo");

        var reply = await _agent.RunAsync(sesionId, PrefijoNotaDeVoz + transcripcion.Texto, telefono, ct);
        return new AudioChatReply(reply.SesionId, transcripcion.Texto, reply.Respuesta, reply.Pasos, reply.Idioma);
    }
}
