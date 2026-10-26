namespace DpmAgent.Api.Agent;

/// <summary>Traduce los fallos al ejecutar el agente a respuestas HTTP, igual para texto y para voz.</summary>
public static class RespuestasAgente
{
    public static async Task<IResult> EjecutarAsync(Func<Task<IResult>> accion)
    {
        try
        {
            return await accion();
        }
        catch (EntradaRechazadaException ex)
        {
            return ex.StatusCode == StatusCodes.Status400BadRequest
                ? Results.BadRequest(new { error = ex.Message })
                : Results.Json(new { error = ex.Message }, statusCode: ex.StatusCode);
        }
        catch (SesionInvalidaException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("clave", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem($"El servicio de Meta no respondió: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
