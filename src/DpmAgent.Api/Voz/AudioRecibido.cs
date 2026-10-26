using System.Buffers.Binary;
using System.Text;

namespace DpmAgent.Api.Voz;

public enum FormatoAudio
{
    Desconocido,
    Ogg,
    WebM,
    Mp3,
    Mp4,
    Wav,
}

/// <summary>
/// Nota de voz recibida, ya validada. El formato se detecta por la firma de los bytes,
/// no por el Content-Type que manda el cliente.
/// </summary>
public sealed class AudioRecibido
{
    private AudioRecibido(byte[] datos, FormatoAudio formato, double? duracion)
    {
        Datos = datos;
        Formato = formato;
        DuracionSegundos = duracion;
    }

    public byte[] Datos { get; }

    public FormatoAudio Formato { get; }

    /// <summary>Duración medida en el propio archivo (Ogg y WAV). Null si el formato no la trae de forma fiable.</summary>
    public double? DuracionSegundos { get; }

    public string ContentType => Formato switch
    {
        FormatoAudio.Ogg => "audio/ogg",
        FormatoAudio.WebM => "audio/webm",
        FormatoAudio.Mp3 => "audio/mpeg",
        FormatoAudio.Mp4 => "audio/mp4",
        FormatoAudio.Wav => "audio/wav",
        _ => "application/octet-stream",
    };

    public string NombreArchivo => Formato switch
    {
        FormatoAudio.Ogg => "nota.ogg",
        FormatoAudio.WebM => "nota.webm",
        FormatoAudio.Mp3 => "nota.mp3",
        FormatoAudio.Mp4 => "nota.m4a",
        FormatoAudio.Wav => "nota.wav",
        _ => "nota.bin",
    };

    /// <summary>
    /// Valida tamaño, formato y duración. Lanza <see cref="AudioInvalidoException"/> con un mensaje para el usuario.
    /// </summary>
    public static AudioRecibido Validar(byte[] datos, TranscripcionOptions limites)
    {
        if (datos.Length == 0)
            throw new AudioInvalidoException("La nota de voz está vacía.");

        if (datos.Length > limites.MaxBytes)
            throw new AudioInvalidoException($"La nota de voz pasa del límite de {limites.MaxBytes / 1024 / 1024.0:0.#} MB.");

        var formato = DetectarFormato(datos);
        if (formato == FormatoAudio.Desconocido)
            throw new AudioInvalidoException("Formato de audio no soportado. Use OGG/Opus, WebM, MP3, M4A o WAV.");

        var duracion = formato switch
        {
            FormatoAudio.Ogg => DuracionOgg(datos),
            FormatoAudio.Wav => DuracionWav(datos),
            _ => null,
        };

        if (duracion > limites.MaxDuracionSegundos)
            throw new AudioInvalidoException($"La nota de voz dura más de {limites.MaxDuracionSegundos:0} segundos.");

        return new AudioRecibido(datos, formato, duracion);
    }

    internal static FormatoAudio DetectarFormato(ReadOnlySpan<byte> d)
    {
        if (d.Length < 12)
            return FormatoAudio.Desconocido;

        if (d[..4].SequenceEqual("OggS"u8))
            return FormatoAudio.Ogg;
        if (d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WAVE"u8))
            return FormatoAudio.Wav;
        if (d[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }))
            return FormatoAudio.WebM;
        if (d[4..8].SequenceEqual("ftyp"u8))
            return FormatoAudio.Mp4;
        if (d[..3].SequenceEqual("ID3"u8) || (d[0] == 0xFF && (d[1] & 0xE0) == 0xE0))
            return FormatoAudio.Mp3;

        return FormatoAudio.Desconocido;
    }

    /// <summary>
    /// Duración de un Ogg Opus u Ogg Vorbis: posición de granulado de la última página
    /// dividida entre la frecuencia (Opus siempre cuenta a 48 kHz y descuenta el pre-skip).
    /// </summary>
    internal static double? DuracionOgg(ReadOnlySpan<byte> d)
    {
        // Primer paquete (cabecera de identificación): empieza tras la tabla de segmentos de la primera página.
        if (d.Length < 28)
            return null;
        var inicioPaquete = 27 + d[26];
        if (inicioPaquete + 19 > d.Length)
            return null;
        var cabecera = d[inicioPaquete..];

        double frecuencia;
        long preSkip = 0;
        if (cabecera[..8].SequenceEqual("OpusHead"u8))
        {
            frecuencia = 48000;
            preSkip = BinaryPrimitives.ReadUInt16LittleEndian(cabecera[10..12]);
        }
        else if (cabecera[0] == 1 && cabecera[1..7].SequenceEqual("vorbis"u8))
        {
            frecuencia = BinaryPrimitives.ReadUInt32LittleEndian(cabecera[12..16]);
        }
        else
        {
            return null;
        }

        var ultima = d.LastIndexOf("OggS"u8);
        if (ultima < 0 || ultima + 14 > d.Length || frecuencia <= 0)
            return null;

        var granulado = BinaryPrimitives.ReadInt64LittleEndian(d[(ultima + 6)..(ultima + 14)]);
        if (granulado < 0)
            return null;

        return Math.Max(0, granulado - preSkip) / frecuencia;
    }

    /// <summary>Duración de un WAV: tamaño del bloque de datos entre los bytes por segundo.</summary>
    internal static double? DuracionWav(ReadOnlySpan<byte> d)
    {
        uint bytesPorSegundo = 0;
        var pos = 12;
        while (pos + 8 <= d.Length)
        {
            var id = Encoding.ASCII.GetString(d[pos..(pos + 4)]);
            var tamano = BinaryPrimitives.ReadUInt32LittleEndian(d[(pos + 4)..(pos + 8)]);

            if (id == "fmt " && pos + 16 <= d.Length - 4)
                bytesPorSegundo = BinaryPrimitives.ReadUInt32LittleEndian(d[(pos + 16)..(pos + 20)]);
            else if (id == "data")
                return bytesPorSegundo == 0 ? null : (double)tamano / bytesPorSegundo;

            // Los bloques RIFF se alinean a 2 bytes.
            var siguiente = pos + 8L + tamano + (tamano & 1);
            if (siguiente > d.Length)
                return null;
            pos = (int)siguiente;
        }

        return null;
    }
}

public sealed class AudioInvalidoException(string message) : Agent.EntradaRechazadaException(message);
