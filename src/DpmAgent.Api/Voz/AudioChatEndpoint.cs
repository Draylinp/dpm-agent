using DpmAgent.Api.Agent;
using DpmAgent.Api.Seguridad;
using Microsoft.AspNetCore.Http.Features;

namespace DpmAgent.Api.Voz;

/// <param name="Transcripcion">Lo que se entendió de la nota de voz, para mostrárselo al paciente.</param>
public sealed record AudioChatReply(string SesionId, string Transcripcion, string Respuesta, IReadOnlyList<AgentStep> Pasos, string? Idioma = null);

public static class AudioChatEndpoint
{
    /// <summary>
    /// POST /api/chat/audio (multipart: audio, sesionId, telefono).
    /// Nota de voz → Muse Voice Transcribe → agente. El audio no se guarda en ningún lado.
    /// </summary>
    public static IEndpointRouteBuilder MapAudioChat(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/chat/audio", async (HttpRequest http, ProcesadorNotaVoz procesador, CancellationToken ct) =>
        {
            var archivo = await ArchivoSubido.LeerAsync(http, "audio", procesador.Limites.MaxBytes, "La nota de voz", ct);
            if (archivo.Error is not null)
                return archivo.Error;

            return await RespuestasAgente.EjecutarAsync(async () => Results.Ok(await procesador.ProcesarAsync(
                archivo.Datos!, http.Form()["sesionId"].ToString(), http.Form()["telefono"].ToString(), ct)),
                RespuestasAgente.IdiomaDe(http, http.Form()["idioma"]));
        }).RequireRateLimiting(ProduccionExtensions.PoliticaPublica);

        return app;
    }
}

/// <summary>Lectura de un archivo multipart con límite de tamaño, común a audio e imagen.</summary>
public sealed record ArchivoSubido(byte[]? Datos, IResult? Error)
{
    public static async Task<ArchivoSubido> LeerAsync(HttpRequest http, string campo, long maxBytes, string nombre, CancellationToken ct)
    {
        if (!http.HasFormContentType)
            return new(null, Results.BadRequest(new { error = $"Envíe el archivo como multipart/form-data en el campo '{campo}'." }));

        // El cuerpo nunca puede pasar del límite del archivo más un margen para los otros campos.
        var maxCuerpo = maxBytes + 64 * 1024;
        if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
            limite.MaxRequestBodySize = maxCuerpo;

        IFormCollection form;
        try
        {
            form = await http.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maxCuerpo }, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return new(null, Results.Problem($"{nombre} es demasiado grande.", statusCode: StatusCodes.Status413PayloadTooLarge));
        }

        var archivo = form.Files[campo];
        if (archivo is null)
            return new(null, Results.BadRequest(new { error = $"Falta el archivo en el campo '{campo}'." }));
        if (archivo.Length > maxBytes)
            return new(null, Results.Problem($"{nombre} es demasiado grande.", statusCode: StatusCodes.Status413PayloadTooLarge));

        using var buffer = new MemoryStream((int)archivo.Length);
        await archivo.CopyToAsync(buffer, ct);
        return new(buffer.ToArray(), null);
    }
}

internal static class FormularioExtensions
{
    /// <summary>El formulario ya leído por <see cref="ArchivoSubido.LeerAsync"/>.</summary>
    public static IFormCollection Form(this HttpRequest http) => http.HttpContext.Features.Get<IFormFeature>()?.Form ?? FormCollection.Empty;
}
