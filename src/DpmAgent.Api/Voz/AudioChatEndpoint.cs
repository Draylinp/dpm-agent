using DpmAgent.Api.Agent;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Voz;

/// <param name="Transcripcion">Lo que se entendió de la nota de voz, para mostrárselo al paciente.</param>
public sealed record AudioChatReply(string SesionId, string Transcripcion, string Respuesta, IReadOnlyList<AgentStep> Pasos);

public static class AudioChatEndpoint
{
    /// <summary>Prefijo con el que el agente reconoce que el mensaje viene de una transcripción automática.</summary>
    public const string PrefijoNotaDeVoz = "[Nota de voz] ";

    private const int MaxCaracteres = 2000;

    /// <summary>
    /// POST /api/chat/audio (multipart: audio, sesionId, telefono).
    /// Nota de voz → Muse Voice Transcribe → agente. El audio no se guarda en ningún lado.
    /// </summary>
    public static IEndpointRouteBuilder MapAudioChat(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/audio", async (
            HttpRequest http,
            ITranscriber transcriber,
            ReceptionAgent agent,
            IOptions<TranscripcionOptions> opciones,
            CancellationToken ct) =>
        {
            var limites = opciones.Value;

            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "Envíe la nota de voz como multipart/form-data en el campo 'audio'." });

            // El cuerpo nunca puede pasar del límite del audio más un margen para los otros campos.
            var maxCuerpo = limites.MaxBytes + 64 * 1024;
            if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
                limite.MaxRequestBodySize = maxCuerpo;

            IFormCollection form;
            try
            {
                form = await http.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maxCuerpo }, ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
            {
                return Results.Problem("La nota de voz es demasiado grande.", statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var archivo = form.Files["audio"];
            if (archivo is null)
                return Results.BadRequest(new { error = "Falta el archivo en el campo 'audio'." });

            if (archivo.Length > limites.MaxBytes)
                return Results.Problem("La nota de voz es demasiado grande.", statusCode: StatusCodes.Status413PayloadTooLarge);

            AudioRecibido audio;
            try
            {
                using var buffer = new MemoryStream((int)archivo.Length);
                await archivo.CopyToAsync(buffer, ct);
                audio = AudioRecibido.Validar(buffer.ToArray(), limites);
            }
            catch (AudioInvalidoException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return await RespuestasAgente.EjecutarAsync(async () =>
            {
                var transcripcion = await transcriber.TranscribirAsync(audio, ct);

                if (transcripcion.DuracionSegundos > limites.MaxDuracionSegundos)
                    return Results.BadRequest(new { error = $"La nota de voz dura más de {limites.MaxDuracionSegundos:0} segundos." });

                if (string.IsNullOrWhiteSpace(transcripcion.Texto))
                {
                    return Results.UnprocessableEntity(new
                    {
                        error = "No se entendió la nota de voz. ¿Puede repetirla o escribir el mensaje?",
                    });
                }

                if (transcripcion.Texto.Length > MaxCaracteres)
                    return Results.BadRequest(new { error = "La nota de voz es demasiado larga. Envíe un mensaje más corto." });

                var reply = await agent.RunAsync(
                    form["sesionId"].ToString(),
                    PrefijoNotaDeVoz + transcripcion.Texto,
                    form["telefono"].ToString(),
                    ct);

                return Results.Ok(new AudioChatReply(reply.SesionId, transcripcion.Texto, reply.Respuesta, reply.Pasos));
            });
        });

        return app;
    }
}
