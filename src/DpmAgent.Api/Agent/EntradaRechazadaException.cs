using System.Globalization;
using DpmAgent.Api.Idiomas;

namespace DpmAgent.Api.Agent;

/// <summary>
/// Una entrada del paciente (audio, imagen, texto) que no se puede procesar. Lleva la clave del texto
/// para mostrarlo en el idioma de quien escribe; el código es el HTTP que corresponde en la API.
/// </summary>
public class EntradaRechazadaException : Exception
{
    public EntradaRechazadaException(string clave, int statusCode = StatusCodes.Status400BadRequest, params object[] argumentos)
        : base(Textos.Obtener(clave, Idiomas.Idiomas.Espanol, argumentos))
    {
        Clave = clave;
        StatusCode = statusCode;
        Argumentos = argumentos;
    }

    public string Clave { get; }

    public int StatusCode { get; }

    public object[] Argumentos { get; }

    public string MensajePara(string? idioma) => Textos.Obtener(Clave, idioma, Argumentos);

    /// <summary>Megabytes con un decimal, para los mensajes de límite de tamaño.</summary>
    public static string Megas(long bytes) => (bytes / 1024 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture);
}
