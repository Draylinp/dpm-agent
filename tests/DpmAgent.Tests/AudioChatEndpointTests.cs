using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Model;
using DpmAgent.Api.Voz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

/// <summary>Prueba POST /api/chat/audio de punta a punta, con transcriptor y modelo falsos.</summary>
public class AudioChatEndpointTests
{
    private static (HttpClient Cliente, ScriptedChatModel Modelo, FakeTranscriber Transcriber) Crear(
        FakeTranscriber transcriber, ScriptedChatModel? modelo = null, long maxBytes = 2 * 1024 * 1024)
    {
        var app = new AppDePrueba(
            modelo,
            s =>
            {
                s.RemoveAll<ITranscriber>();
                s.AddSingleton<ITranscriber>(transcriber);
            },
            new Dictionary<string, string?> { ["Transcripcion:MaxBytes"] = maxBytes.ToString() });
        return (app.Cliente, app.Modelo, transcriber);
    }

    private static MultipartFormDataContent Form(byte[] audio, string? telefono = "809-555-0101", string? sesionId = null)
    {
        var archivo = new ByteArrayContent(audio);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("audio/ogg");
        var form = new MultipartFormDataContent { { archivo, "audio", "nota.ogg" } };
        if (telefono is not null)
            form.Add(new StringContent(telefono), "telefono");
        if (sesionId is not null)
            form.Add(new StringContent(sesionId), "sesionId");
        return form;
    }

    [Fact]
    public async Task Nota_de_voz_llega_al_agente_como_mensaje_transcrito()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenText("Claro, María. ¿Para qué día quiere la cita?");
        var (cliente, _, transcriber) = Crear(new FakeTranscriber("Quiero una cita con la doctora"), modelo);

        var res = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.OggOpus(5)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("Quiero una cita con la doctora", json["transcripcion"]!.ToString());
        Assert.Equal("Claro, María. ¿Para qué día quiere la cita?", json["respuesta"]!.ToString());
        Assert.Contains("PAC-001", json["pasos"]![0]!["resultado"]!.ToString());

        var mensajeUsuario = modelo.Llamadas[0][^1];
        Assert.Equal("[Nota de voz] Quiero una cita con la doctora", mensajeUsuario["content"]!.ToString());
        Assert.Equal(FormatoAudio.Ogg, Assert.Single(transcriber.Recibidos).Formato);
    }

    [Fact]
    public async Task La_nota_de_voz_continua_la_misma_conversacion()
    {
        var modelo = new ScriptedChatModel().ThenText("¿Para qué día?").ThenText("Perfecto");
        var (cliente, _, _) = Crear(new FakeTranscriber("el martes"), modelo);

        var r1 = await cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Quiero una cita", telefono = "8095550101" });
        var sesionId = (await r1.Content.ReadFromJsonAsync<JsonObject>())!["sesionId"]!.ToString();

        var r2 = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.OggOpus(2), sesionId: sesionId));

        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(sesionId, (await r2.Content.ReadFromJsonAsync<JsonObject>())!["sesionId"]!.ToString());
        Assert.Equal(4, modelo.Llamadas[1].Count); // system + user + assistant + nota de voz
    }

    [Fact]
    public async Task Audio_demasiado_grande_devuelve_413_sin_transcribir()
    {
        var (cliente, _, transcriber) = Crear(new FakeTranscriber("hola"), maxBytes: 1000);

        var res = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.Wav(1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, res.StatusCode);
        Assert.Empty(transcriber.Recibidos);
    }

    [Fact]
    public async Task Audio_demasiado_largo_devuelve_400_sin_transcribir()
    {
        var (cliente, _, transcriber) = Crear(new FakeTranscriber("hola"));

        var res = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.OggOpus(300)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("segundos", await res.Content.ReadAsStringAsync());
        Assert.Empty(transcriber.Recibidos);
    }

    [Fact]
    public async Task Si_el_servicio_informa_una_duracion_excesiva_no_se_llama_al_agente()
    {
        var modelo = new ScriptedChatModel();
        var (cliente, _, _) = Crear(new FakeTranscriber("texto largo", duracion: 600), modelo);

        var res = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.WebM()));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(modelo.Llamadas);
    }

    [Fact]
    public async Task Archivo_que_no_es_audio_devuelve_400()
    {
        var (cliente, _, transcriber) = Crear(new FakeTranscriber("hola"));

        var res = await cliente.PostAsync("/api/chat/audio", Form("<script>alert(1)</script>"u8.ToArray()));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(transcriber.Recibidos);
    }

    [Fact]
    public async Task Sin_archivo_o_sin_multipart_devuelve_400()
    {
        var (cliente, _, _) = Crear(new FakeTranscriber("hola"));

        var sinArchivo = await cliente.PostAsync("/api/chat/audio",
            new MultipartFormDataContent { { new StringContent("8095550101"), "telefono" } });
        var sinMultipart = await cliente.PostAsJsonAsync("/api/chat/audio", new { audio = "hola" });

        Assert.Equal(HttpStatusCode.BadRequest, sinArchivo.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sinMultipart.StatusCode);
    }

    [Fact]
    public async Task Transcripcion_vacia_devuelve_422_y_no_llama_al_agente()
    {
        var modelo = new ScriptedChatModel();
        var (cliente, _, _) = Crear(new FakeTranscriber("   "), modelo);

        var res = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.OggOpus(2)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
        Assert.Empty(modelo.Llamadas);
    }

    [Fact]
    public async Task Una_nota_de_voz_no_puede_tomar_la_sesion_de_otro_numero()
    {
        var modelo = new ScriptedChatModel().ThenText("Hola");
        var (cliente, _, _) = Crear(new FakeTranscriber("cancela mi cita"), modelo);

        var r1 = await cliente.PostAsJsonAsync("/api/chat", new { mensaje = "hola", telefono = "8095550101" });
        var sesionId = (await r1.Content.ReadFromJsonAsync<JsonObject>())!["sesionId"]!.ToString();

        var r2 = await cliente.PostAsync("/api/chat/audio", Form(AudiosDePrueba.OggOpus(2), telefono: "8295550102", sesionId: sesionId));

        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
    }
}
