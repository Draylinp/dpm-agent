namespace DpmAgent.Api.Carnet;

/// <summary>
/// Foto recibida, validada por la firma de sus bytes (JPEG, PNG o WebP).
/// Solo vive en memoria mientras se lee.
/// </summary>
public sealed class ImagenRecibida
{
    private ImagenRecibida(byte[] datos, string contentType)
    {
        Datos = datos;
        ContentType = contentType;
    }

    public byte[] Datos { get; }

    public string ContentType { get; }

    public string DataUrl => $"data:{ContentType};base64,{Convert.ToBase64String(Datos)}";

    public static ImagenRecibida Validar(byte[] datos, long maxBytes)
    {
        if (datos.Length == 0)
            throw new ImagenInvalidaException("entrada.imagen_vacia");

        if (datos.Length > maxBytes)
            throw new ImagenInvalidaException("entrada.imagen_grande", Agent.EntradaRechazadaException.Megas(maxBytes));

        var tipo = DetectarTipo(datos)
                   ?? throw new ImagenInvalidaException("entrada.imagen_formato");

        return new ImagenRecibida(datos, tipo);
    }

    internal static string? DetectarTipo(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
            return "image/jpeg";
        if (d.Length >= 8 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (d.Length >= 12 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}

public sealed class ImagenInvalidaException(string clave, params object[] argumentos)
    : Agent.EntradaRechazadaException(clave, StatusCodes.Status400BadRequest, argumentos);
