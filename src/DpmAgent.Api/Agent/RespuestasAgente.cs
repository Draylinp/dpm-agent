using DpmAgent.Api.Idiomas;

namespace DpmAgent.Api.Agent;

/// <summary>Traduce los fallos al ejecutar el agente a respuestas HTTP, igual para texto y para voz.</summary>
public static class RespuestasAgente
{
    /// <param name="idioma">Idioma para los mensajes de error (el de la interfaz o el de la conversación).</param>
    public static async Task<IResult> EjecutarAsync(Func<Task<IResult>> accion, string? idioma = null)
    {
        try
        {
            return await accion();
        }
        catch (EntradaRechazadaException ex)
        {
            var mensaje = ex.MensajePara(idioma);
            return ex.StatusCode == StatusCodes.Status400BadRequest
                ? Results.BadRequest(new { error = mensaje })
                : Results.Json(new { error = mensaje }, statusCode: ex.StatusCode);
        }
        // Al paciente le llega un texto amable en su idioma; el detalle técnico no sale del servidor.
        catch (SesionInvalidaException)
        {
            return Results.Json(new { error = Textos.Obtener("entrada.sesion_invalida", idioma) }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("clave", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new { error = Textos.Obtener("error.no_disponible", idioma) }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException { InnerException: TimeoutException })
        {
            return Results.Json(new { error = Textos.Obtener("error.modelo", idioma) }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Idioma de la petición: el que manda la interfaz, o el primero de Accept-Language.</summary>
    public static string? IdiomaDe(HttpRequest http, string? explicito = null) =>
        Idiomas.Idiomas.Normalizar(explicito)
        ?? Idiomas.Idiomas.Normalizar(http.Headers.AcceptLanguage.ToString().Split(',', ';').FirstOrDefault());
}
