using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Model;

namespace DpmAgent.Tests;

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}

/// <summary>
/// Modelo falso: cada llamada ejecuta el siguiente paso del guion
/// y guarda los mensajes que recibió.
/// </summary>
public sealed class ScriptedChatModel : IChatModel
{
    private readonly Queue<Func<IReadOnlyList<JsonObject>, JsonObject>> _guion = new();

    public List<IReadOnlyList<JsonObject>> Llamadas { get; } = [];

    public ScriptedChatModel Then(Func<IReadOnlyList<JsonObject>, JsonObject> paso)
    {
        _guion.Enqueue(paso);
        return this;
    }

    public ScriptedChatModel ThenTool(string nombre, object args) =>
        Then(_ => ToolCall(nombre, args));

    public ScriptedChatModel ThenText(string texto) =>
        Then(_ => new JsonObject { ["role"] = "assistant", ["content"] = texto });

    public Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
    {
        Llamadas.Add(messages.Select(m => m.DeepClone().AsObject()).ToList());
        if (_guion.Count == 0)
            throw new InvalidOperationException("El guion del modelo falso se agotó.");
        return Task.FromResult(_guion.Dequeue()(messages));
    }

    public static JsonObject ToolCall(string nombre, object args) => new()
    {
        ["role"] = "assistant",
        ["content"] = null,
        ["tool_calls"] = new JsonArray(new JsonObject
        {
            ["id"] = $"call_{nombre}_{Guid.NewGuid():N}",
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = nombre,
                ["arguments"] = JsonSerializer.Serialize(args),
            },
        }),
    };
}

/// <summary>Transcriptor falso: devuelve un texto fijo y guarda el audio que recibió.</summary>
public sealed class FakeTranscriber(string texto, double? duracion = null) : DpmAgent.Api.Voz.ITranscriber
{
    public List<DpmAgent.Api.Voz.AudioRecibido> Recibidos { get; } = [];

    public Task<DpmAgent.Api.Voz.Transcripcion> TranscribirAsync(DpmAgent.Api.Voz.AudioRecibido audio, CancellationToken ct)
    {
        Recibidos.Add(audio);
        return Task.FromResult(new DpmAgent.Api.Voz.Transcripcion(texto, duracion));
    }
}

/// <summary>Genera audios mínimos válidos para las pruebas.</summary>
public static class AudiosDePrueba
{
    /// <summary>Ogg Opus de la duración indicada (solo cabeceras; el contenido no importa).</summary>
    public static byte[] OggOpus(double segundos, ushort preSkip = 312)
    {
        var opusHead = new byte[19];
        "OpusHead"u8.CopyTo(opusHead);
        opusHead[8] = 1;
        opusHead[9] = 1;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(opusHead.AsSpan(10), preSkip);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(opusHead.AsSpan(12), 48000);

        var granulo = (long)(segundos * 48000) + preSkip;
        return [.. PaginaOgg(0, 0, opusHead), .. PaginaOgg(4, granulo, [0xF8])];
    }

    /// <summary>WAV PCM mono de 16 kHz y 16 bits con la duración indicada.</summary>
    public static byte[] Wav(double segundos)
    {
        const int bytesPorSegundo = 16000 * 2;
        var datos = (int)(segundos * bytesPorSegundo);
        var wav = new byte[44 + datos];
        var s = wav.AsSpan();
        "RIFF"u8.CopyTo(s);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(s[4..], 36 + datos);
        "WAVE"u8.CopyTo(s[8..]);
        "fmt "u8.CopyTo(s[12..]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(s[16..], 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(s[20..], 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(s[22..], 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(s[24..], 16000);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(s[28..], bytesPorSegundo);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(s[32..], 2);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(s[34..], 16);
        "data"u8.CopyTo(s[36..]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(s[40..], datos);
        return wav;
    }

    public static byte[] WebM() => [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81, 0x01];

    private static byte[] PaginaOgg(byte tipo, long granulo, byte[] carga)
    {
        var pagina = new byte[27 + 1 + carga.Length];
        "OggS"u8.CopyTo(pagina);
        pagina[5] = tipo;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(pagina.AsSpan(6), granulo);
        pagina[26] = 1;
        pagina[27] = (byte)carga.Length;
        carga.CopyTo(pagina, 28);
        return pagina;
    }
}

/// <summary>Lector de carnet falso con un resultado fijo.</summary>
public sealed class FakeLectorCarnet(DpmAgent.Api.Carnet.LecturaCarnet resultado) : DpmAgent.Api.Carnet.ILectorCarnet
{
    public int Lecturas { get; private set; }

    public Task<DpmAgent.Api.Carnet.LecturaCarnet> LeerAsync(DpmAgent.Api.Carnet.ImagenRecibida imagen, CancellationToken ct)
    {
        Lecturas++;
        return Task.FromResult(resultado);
    }
}

public static class ImagenesDePrueba
{
    public static byte[] Jpeg(int tamano = 64)
    {
        var datos = new byte[tamano];
        datos[0] = 0xFF;
        datos[1] = 0xD8;
        datos[2] = 0xFF;
        return datos;
    }
}

/// <summary>Reloj que se puede mover, para probar vencimientos y recordatorios.</summary>
public sealed class RelojAjustable(DateTimeOffset inicio) : TimeProvider
{
    public DateTimeOffset Ahora { get; set; } = inicio;

    public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;

    public override DateTimeOffset GetUtcNow() => Ahora.ToUniversalTime();
}

/// <summary>Canal de WhatsApp falso: guarda lo enviado o falla a propósito.</summary>
public sealed class CanalWhatsAppFalso : DpmAgent.Api.Canales.IMessageChannel
{
    public string Canal => DpmAgent.Api.Canales.CanalesConocidos.WhatsApp;

    public bool Fallar { get; set; }

    public List<DpmAgent.Api.Canales.MensajeParaEnviar> Enviados { get; } = [];

    public Task EnviarAsync(DpmAgent.Api.Canales.MensajeParaEnviar mensaje, CancellationToken ct)
    {
        if (Fallar)
            throw new HttpRequestException("WhatsApp no respondió");
        Enviados.Add(mensaje);
        return Task.CompletedTask;
    }
}
