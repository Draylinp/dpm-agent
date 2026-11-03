using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;
using DpmAgent.Api.Voz;

namespace DpmAgent.Eval;

/// <summary>
/// Modelo falso que responde según el guion del caso. Si el guion se acaba, es un error del caso.
/// </summary>
public sealed class ModeloGuionado(IEnumerable<PasoGuion> guion, Func<string, string> reemplazar) : IChatModel
{
    private readonly Queue<PasoGuion> _pasos = new(guion);
    private int _llamada;

    public Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
    {
        if (!_pasos.TryDequeue(out var paso))
            throw new InvalidOperationException("El guion del caso se agotó antes de terminar la conversación.");

        _llamada++;
        if (paso.Herramientas is not { Count: > 0 } llamadas)
            return Task.FromResult(new JsonObject { ["role"] = "assistant", ["content"] = reemplazar(paso.Texto ?? "") });

        var calls = new JsonArray();
        var i = 0;
        foreach (var l in llamadas)
        {
            calls.Add(new JsonObject
            {
                ["id"] = $"call_{_llamada}_{i++}",
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = l.Nombre,
                    ["arguments"] = reemplazar(l.Args?.ToJsonString() ?? "{}"),
                },
            });
        }

        return Task.FromResult(new JsonObject { ["role"] = "assistant", ["content"] = null, ["tool_calls"] = calls });
    }
}

/// <summary>
/// Entrada que "llega" en el turno actual: el texto de una nota de voz o los datos de una foto.
/// La evaluación no tiene audio ni imágenes reales: simula lo que devolverían Muse Voice Transcribe y Muse Spark.
/// </summary>
public sealed class EntradaSimulada : ITranscriber, ILectorCarnet
{
    public string? Transcripcion { get; set; }

    public FotoCarnet? Foto { get; set; }

    public Task<Transcripcion> TranscribirAsync(AudioRecibido audio, CancellationToken ct) =>
        Task.FromResult(new Transcripcion(Transcripcion ?? "", null));

    public Task<LecturaCarnet> LeerAsync(ImagenRecibida imagen, CancellationToken ct)
    {
        if (Foto is null || !Foto.EsCarnet)
            return Task.FromResult(new LecturaCarnet(false, new DatosCarnet(null, null, null)));

        // Igual que MetaLectorCarnet: lo leído pasa por la misma limpieza de datos no confiables.
        var datos = DatosCarnet.Limpiar(Foto.Nombre, Foto.Ars, Foto.Afiliado);
        return Task.FromResult(new LecturaCarnet(!datos.Vacio, datos));
    }

    /// <summary>Un Ogg Opus mínimo de 3 segundos: basta para pasar la validación de audio.</summary>
    public static byte[] Audio()
    {
        static byte[] Pagina(byte tipo, long granulo, byte[] carga)
        {
            var p = new byte[28 + carga.Length];
            "OggS"u8.CopyTo(p);
            p[5] = tipo;
            BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(6), granulo);
            p[26] = 1;
            p[27] = (byte)carga.Length;
            carga.CopyTo(p, 28);
            return p;
        }

        var cabecera = new byte[19];
        "OpusHead"u8.CopyTo(cabecera);
        cabecera[8] = 1;
        cabecera[9] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(cabecera.AsSpan(10), 312);
        BinaryPrimitives.WriteUInt32LittleEndian(cabecera.AsSpan(12), 48000);
        return [.. Pagina(2, 0, cabecera), .. Pagina(4, 3 * 48000 + 312, [0xF8])];
    }

    /// <summary>Un JPEG mínimo: basta para pasar la validación de imagen.</summary>
    public static byte[] Imagen() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 0];
}

internal static class JsonGuion
{
    public static string Serializar(object o) => JsonSerializer.Serialize(o, Casos.Json);
}
