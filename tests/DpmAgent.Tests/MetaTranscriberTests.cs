using System.Net;
using DpmAgent.Api.Model;
using DpmAgent.Api.Voz;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

public class MetaTranscriberTests
{
    private sealed class HandlerGrabador(HttpStatusCode status, string respuesta) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }

        public string? Cuerpo { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticion = request;
            Cuerpo = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(respuesta) };
        }
    }

    private static (MetaTranscriber, HandlerGrabador) Crear(
        HttpStatusCode status, string respuesta, string? apiKey = "clave-de-prueba", string? idioma = "es")
    {
        var handler = new HandlerGrabador(status, respuesta);
        var t = new MetaTranscriber(
            new HttpClient(handler),
            Options.Create(new MetaOptions { ApiKey = apiKey, BaseUrl = "https://api.ejemplo.test/v1" }),
            Options.Create(new TranscripcionOptions { Idioma = idioma }),
            NullLogger<MetaTranscriber>.Instance);
        return (t, handler);
    }

    private static AudioRecibido Audio() => AudioRecibido.Validar(AudiosDePrueba.OggOpus(4), new TranscripcionOptions());

    [Fact]
    public async Task Envia_multipart_con_el_modelo_y_el_idioma()
    {
        var (t, handler) = Crear(HttpStatusCode.OK, """{"text":" Quiero una cita para mañana ","duration":4.1}""");

        var r = await t.TranscribirAsync(Audio(), CancellationToken.None);

        Assert.Equal("Quiero una cita para mañana", r.Texto);
        Assert.Equal(4.1, r.DuracionSegundos);
        Assert.Equal(HttpMethod.Post, handler.Peticion!.Method);
        Assert.Equal("https://api.ejemplo.test/v1/audio/transcriptions", handler.Peticion.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Peticion.Headers.Authorization!.Scheme);
        Assert.Equal("multipart/form-data", handler.Peticion.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("name=model", handler.Cuerpo);
        Assert.Contains("muse-voice-transcribe-1.0", handler.Cuerpo);
        Assert.Contains("name=language", handler.Cuerpo);
        Assert.Contains("name=file; filename=nota.ogg", handler.Cuerpo);
        Assert.Contains("Content-Type: audio/ogg", handler.Cuerpo);
    }

    [Fact]
    public async Task Sin_idioma_configurado_el_servicio_detecta_el_idioma()
    {
        var (t, handler) = Crear(HttpStatusCode.OK, """{"text":"I need an appointment"}""", idioma: null);

        await t.TranscribirAsync(Audio(), CancellationToken.None);

        Assert.DoesNotContain("name=language", handler.Cuerpo);
    }

    [Fact]
    public async Task La_duracion_es_opcional_en_la_respuesta()
    {
        var (t, _) = Crear(HttpStatusCode.OK, """{"text":"hola"}""");

        var r = await t.TranscribirAsync(Audio(), CancellationToken.None);

        Assert.Equal("hola", r.Texto);
        Assert.Null(r.DuracionSegundos);
    }

    [Fact]
    public async Task Un_error_del_servicio_se_reporta_como_fallo_http()
    {
        var (t, _) = Crear(HttpStatusCode.TooManyRequests, """{"error":"rate limit"}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => t.TranscribirAsync(Audio(), CancellationToken.None));
        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
    }

    [Fact]
    public async Task Sin_clave_no_llama_al_servicio()
    {
        var anterior = Environment.GetEnvironmentVariable("META_API_KEY");
        Environment.SetEnvironmentVariable("META_API_KEY", null);
        try
        {
            var (t, handler) = Crear(HttpStatusCode.OK, """{"text":"hola"}""", apiKey: null);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => t.TranscribirAsync(Audio(), CancellationToken.None));
            Assert.Contains("clave", ex.Message);
            Assert.Null(handler.Peticion);
        }
        finally
        {
            Environment.SetEnvironmentVariable("META_API_KEY", anterior);
        }
    }
}
