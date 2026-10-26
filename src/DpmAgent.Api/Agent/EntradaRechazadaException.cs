namespace DpmAgent.Api.Agent;

/// <summary>
/// Una entrada del paciente (audio, imagen, texto) que no se puede procesar.
/// El mensaje es para el paciente; el código es el HTTP que corresponde en la API.
/// </summary>
public class EntradaRechazadaException(string message, int statusCode = StatusCodes.Status400BadRequest) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
