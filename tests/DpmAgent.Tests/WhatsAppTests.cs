using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Canales.WhatsApp;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Voz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

public class WebhookWhatsAppTests
{
    [Fact]
    public void Valida_la_firma_hmac_del_cuerpo()
    {
        var cuerpo = Encoding.UTF8.GetBytes("""{"entry":[]}""");
        var firma = WebhookWhatsApp.Firmar(cuerpo, "secreto");

        Assert.True(WebhookWhatsApp.FirmaValida(cuerpo, firma, "secreto"));
        Assert.True(WebhookWhatsApp.FirmaValida(cuerpo, firma.ToUpperInvariant().Replace("SHA256=", "sha256="), "secreto"));
        Assert.False(WebhookWhatsApp.FirmaValida(cuerpo, firma, "otro-secreto"));
        Assert.False(WebhookWhatsApp.FirmaValida(Encoding.UTF8.GetBytes("""{"entry":[1]}"""), firma, "secreto"));
        Assert.False(WebhookWhatsApp.FirmaValida(cuerpo, null, "secreto"));
        Assert.False(WebhookWhatsApp.FirmaValida(cuerpo, "sha256=no-es-hex", "secreto"));
        Assert.False(WebhookWhatsApp.FirmaValida(cuerpo, "md5=abc", "secreto"));
    }

    [Fact]
    public void Lee_texto_audio_imagen_botones_e_ignora_estados()
    {
        var cuerpo = Encoding.UTF8.GetBytes(Payloads.Varios("PNID"));

        var mensajes = WebhookWhatsApp.LeerMensajes(cuerpo);

        Assert.Equal(["text", "audio", "image", "text", "otro"], mensajes.Select(m => m.Tipo));
        Assert.Equal("Quiero una cita", mensajes[0].Texto);
        Assert.Equal("MEDIA-AUDIO", mensajes[1].MediaId);
        Assert.Equal("MEDIA-IMG", mensajes[2].MediaId);
        Assert.Equal("CONFIRMO", mensajes[3].Texto);
        Assert.All(mensajes, m => Assert.Equal("PNID", m.PhoneNumberId));
        Assert.All(mensajes, m => Assert.Equal("18095550101", m.Telefono));
        Assert.Empty(WebhookWhatsApp.LeerMensajes(Encoding.UTF8.GetBytes("no es json")));
    }

    [Fact]
    public async Task Canal_usa_plantilla_si_esta_configurada_y_texto_si_no()
    {
        var api = new WhatsAppApiFalsa();
        var canal = new WhatsAppCloudChannel(api, Options.Create(new WhatsAppOptions
        {
            PhoneNumberId = "PNID",
            Plantillas = new() { ["recordatorio"] = "recordatorio_cita", ["oferta"] = "" },
        }));

        await canal.EnviarAsync(new MensajeParaEnviar(1, "wa-8095550101", "8095550101", "texto", "recordatorio",
            null, "recordatorio", ["María", "Dra. Ana", "martes", "9:00 a. m.", "CITA-1"]), CancellationToken.None);
        await canal.EnviarAsync(new MensajeParaEnviar(2, "wa-8095550101", "8095550101", "Hola", "oferta",
            "PNID-2", "oferta", ["x"]), CancellationToken.None);
        await canal.EnviarAsync(new MensajeParaEnviar(3, "wa-8095550101", "8095550101", "Respuesta", "respuesta"), CancellationToken.None);

        var plantilla = Assert.Single(api.Plantillas);
        Assert.Equal(("PNID", "18095550101", "recordatorio_cita", "es"), (plantilla.Remitente, plantilla.Destino, plantilla.Plantilla, plantilla.Idioma));
        Assert.Equal(5, plantilla.Parametros.Count);
        Assert.Equal([("PNID-2", "18095550101", "Hola"), ("PNID", "18095550101", "Respuesta")], api.Textos);
    }
}

public class WhatsAppCloudApiTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Peticion, string? Cuerpo)> Peticiones { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticiones.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return responder(request);
        }
    }

    private static (WhatsAppCloudApi, Handler) Crear(Func<HttpRequestMessage, HttpResponseMessage> responder, string? token = "token")
    {
        var handler = new Handler(responder);
        var api = new WhatsAppCloudApi(new HttpClient(handler),
            Options.Create(new WhatsAppOptions { AccessToken = token, ApiVersion = "v21.0" }), NullLogger<WhatsAppCloudApi>.Instance);
        return (api, handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json) };

    [Fact]
    public async Task Envia_texto_y_plantilla_con_el_formato_de_la_cloud_api()
    {
        var (api, handler) = Crear(_ => Json("""{"messages":[{"id":"wamid.1"}]}"""));

        await api.EnviarTextoAsync("PNID", "18095550101", "Hola", CancellationToken.None);
        await api.EnviarPlantillaAsync("PNID", "18095550101", "recordatorio_cita", "es", ["María", "9:00"], CancellationToken.None);

        var (texto, cuerpoTexto) = handler.Peticiones[0];
        Assert.Equal("https://graph.facebook.com/v21.0/PNID/messages", texto.RequestUri!.ToString());
        Assert.Equal("Bearer token", texto.Headers.Authorization!.ToString());
        var jt = JsonNode.Parse(cuerpoTexto!)!;
        Assert.Equal("whatsapp", jt["messaging_product"]!.ToString());
        Assert.Equal("18095550101", jt["to"]!.ToString());
        Assert.Equal("Hola", jt["text"]!["body"]!.ToString());

        var jp = JsonNode.Parse(handler.Peticiones[1].Cuerpo!)!;
        Assert.Equal("template", jp["type"]!.ToString());
        Assert.Equal("recordatorio_cita", jp["template"]!["name"]!.ToString());
        Assert.Equal("es", jp["template"]!["language"]!["code"]!.ToString());
        Assert.Equal("9:00", jp["template"]!["components"]![0]!["parameters"]![1]!["text"]!.ToString());
    }

    [Fact]
    public async Task Descarga_media_en_dos_pasos_y_respeta_el_limite()
    {
        var (api, handler) = Crear(r => r.RequestUri!.Host == "lookaside.ejemplo.test"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3, 4]) }
            : Json("""{"url":"https://lookaside.ejemplo.test/archivo","mime_type":"audio/ogg","file_size":4}"""));

        var media = await api.DescargarMediaAsync("MEDIA-1", 100, CancellationToken.None);

        Assert.Equal([1, 2, 3, 4], media.Datos);
        Assert.Equal("audio/ogg", media.MimeType);
        Assert.Equal("https://graph.facebook.com/v21.0/MEDIA-1", handler.Peticiones[0].Peticion.RequestUri!.ToString());
        Assert.Equal("Bearer token", handler.Peticiones[1].Peticion.Headers.Authorization!.ToString());

        await Assert.ThrowsAsync<ArchivoDemasiadoGrandeException>(() => api.DescargarMediaAsync("MEDIA-1", 3, CancellationToken.None));
    }

    [Fact]
    public async Task Un_error_de_meta_trae_su_mensaje_y_sin_token_no_llama()
    {
        var (api, _) = Crear(_ => Json("""{"error":{"message":"Re-engagement message","code":131047}}""", HttpStatusCode.BadRequest));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => api.EnviarTextoAsync("PNID", "1809", "Hola", CancellationToken.None));
        Assert.Contains("Re-engagement message", ex.Message);

        var (sinToken, handler) = Crear(_ => Json("{}"), token: null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sinToken.EnviarTextoAsync("PNID", "1809", "Hola", CancellationToken.None));
        Assert.Empty(handler.Peticiones);
    }
}

/// <summary>Webhook y procesador de punta a punta: Meta → agente → respuesta por WhatsApp.</summary>
public class WhatsAppFlujoTests
{
    private const string Secreto = "secreto-de-prueba";

    private static (AppDePrueba App, WhatsAppApiFalsa Api) Crear(
        ScriptedChatModel modelo, FakeTranscriber? transcriber = null, FakeLectorCarnet? lector = null, string? secreto = Secreto)
    {
        var api = new WhatsAppApiFalsa();
        var app = new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<IWhatsAppCloudApi>();
            s.AddSingleton<IWhatsAppCloudApi>(api);
            if (transcriber is not null)
            {
                s.RemoveAll<ITranscriber>();
                s.AddSingleton<ITranscriber>(transcriber);
            }
            if (lector is not null)
            {
                s.RemoveAll<ILectorCarnet>();
                s.AddSingleton<ILectorCarnet>(lector);
            }
        }, new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = secreto,
            ["WhatsApp:VerifyToken"] = "token-verificacion",
            ["WhatsApp:PhoneNumberId"] = "PNID",
            ["WhatsApp:ProcesadorActivo"] = "false",
        });
        return (app, api);
    }

    private static async Task<HttpResponseMessage> EnviarWebhook(AppDePrueba app, string json, string? firma = null)
    {
        var cuerpo = Encoding.UTF8.GetBytes(json);
        var contenido = new ByteArrayContent(cuerpo);
        contenido.Headers.ContentType = new("application/json");
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = contenido };
        peticion.Headers.Add("X-Hub-Signature-256", firma ?? WebhookWhatsApp.Firmar(cuerpo, Secreto));
        return await app.Cliente.SendAsync(peticion);
    }

    /// <summary>Procesa lo recibido y despacha las respuestas, como lo harían las tareas en segundo plano.</summary>
    private static async Task Procesar(AppDePrueba app)
    {
        await app.Services.GetRequiredService<ProcesadorWhatsApp>().ProcesarPendientesAsync(CancellationToken.None);
        await app.EnAmbito(sp => sp.GetRequiredService<DespachadorMensajes>().DespacharPendientesAsync(CancellationToken.None));
    }

    private static Task<List<Api.Data.Entidades.MensajeEntrante>> Entrantes(AppDePrueba app) =>
        app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().MensajesEntrantes.IgnoreQueryFilters().ToListAsync());

    [Fact]
    public async Task Verificacion_de_la_suscripcion()
    {
        var (app, _) = Crear(new ScriptedChatModel());
        using var _app = app;

        var ok = await app.Cliente.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=token-verificacion&hub.challenge=12345");
        var mal = await app.Cliente.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe&hub.verify_token=otro&hub.challenge=12345");

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("12345", await ok.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, mal.StatusCode);
    }

    [Fact]
    public async Task Rechaza_firmas_invalidas_y_sin_secreto_no_acepta_nada()
    {
        var (app, _) = Crear(new ScriptedChatModel());
        using var _app = app;
        var (sinSecreto, _) = Crear(new ScriptedChatModel(), secreto: null);
        using var _sinSecreto = sinSecreto;

        var invalida = await EnviarWebhook(app, Payloads.Texto("PNID", "wamid.1", "hola"), firma: "sha256=" + new string('0', 64));
        var noConfigurado = await EnviarWebhook(sinSecreto, Payloads.Texto("PNID", "wamid.1", "hola"));

        Assert.Equal(HttpStatusCode.Unauthorized, invalida.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noConfigurado.StatusCode);
        Assert.Empty(await Entrantes(app));
    }

    [Fact]
    public async Task Texto_llega_al_agente_y_la_respuesta_sale_por_whatsapp()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenText("Hola, María. ¿Para qué día quiere la cita?");
        var (app, api) = Crear(modelo);
        using var _app = app;

        var res = await EnviarWebhook(app, Payloads.Texto("PNID", "wamid.1", "Quiero una cita"));
        await Procesar(app);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Quiero una cita", modelo.Llamadas[0][^1]["content"]!.ToString());
        Assert.Contains("PAC-001", modelo.Llamadas[1][^1]["content"]!.ToString());
        Assert.Equal([("PNID", "18095550101", "Hola, María. ¿Para qué día quiere la cita?")], api.Textos);
        Assert.Equal("procesado", Assert.Single(await Entrantes(app)).Estado);
    }

    [Fact]
    public async Task Los_reintentos_de_meta_no_se_procesan_dos_veces()
    {
        var (app, api) = Crear(new ScriptedChatModel().ThenText("Hola"));
        using var _app = app;

        await EnviarWebhook(app, Payloads.Texto("PNID", "wamid.repetido", "hola"));
        await EnviarWebhook(app, Payloads.Texto("PNID", "wamid.repetido", "hola"));
        await Procesar(app);

        Assert.Single(await Entrantes(app));
        Assert.Single(api.Textos);
    }

    [Fact]
    public async Task Estados_de_entrega_y_numeros_desconocidos_se_ignoran()
    {
        var (app, _) = Crear(new ScriptedChatModel());
        using var _app = app;

        var estados = await EnviarWebhook(app, Payloads.Estado("PNID"));
        var otroNumero = await EnviarWebhook(app, Payloads.Texto("OTRO-PNID", "wamid.2", "hola"));

        Assert.Equal(HttpStatusCode.OK, estados.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otroNumero.StatusCode);
        Assert.Empty(await Entrantes(app));
    }

    [Fact]
    public async Task Nota_de_voz_se_descarga_y_transcribe()
    {
        var modelo = new ScriptedChatModel().ThenText("Claro, ¿para qué día?");
        var (app, api) = Crear(modelo, transcriber: new FakeTranscriber("Quiero cita con la pediatra"));
        using var _app = app;
        api.Archivo = AudiosDePrueba.OggOpus(4);

        await EnviarWebhook(app, Payloads.Audio("PNID", "wamid.3", "MEDIA-AUDIO"));
        await Procesar(app);

        Assert.Equal("[Nota de voz] Quiero cita con la pediatra", modelo.Llamadas[0][^1]["content"]!.ToString());
        Assert.Equal("Claro, ¿para qué día?", Assert.Single(api.Textos).Texto);
    }

    [Fact]
    public async Task Nota_de_voz_demasiado_larga_recibe_una_explicacion_sin_llamar_al_agente()
    {
        var modelo = new ScriptedChatModel();
        var (app, api) = Crear(modelo, transcriber: new FakeTranscriber("x"));
        using var _app = app;
        api.Archivo = AudiosDePrueba.OggOpus(600);

        await EnviarWebhook(app, Payloads.Audio("PNID", "wamid.4", "MEDIA-AUDIO"));
        await Procesar(app);

        Assert.Empty(modelo.Llamadas);
        Assert.Contains("segundos", Assert.Single(api.Textos).Texto);
    }

    [Fact]
    public async Task Foto_del_carnet_queda_pendiente_y_el_agente_pide_confirmacion()
    {
        var modelo = new ScriptedChatModel().ThenText("Leí ARS Nueva, afiliado AF-1. ¿Correcto?");
        var lector = new FakeLectorCarnet(new LecturaCarnet(true, new DatosCarnet("María Rodríguez", "ARS Nueva", "AF-1")));
        var (app, api) = Crear(modelo, lector: lector);
        using var _app = app;
        api.Archivo = ImagenesDePrueba.Jpeg();

        await EnviarWebhook(app, Payloads.Imagen("PNID", "wamid.5", "MEDIA-IMG"));
        await Procesar(app);

        var pendiente = Assert.Single(await app.ConGateway(g => g.ListarCarnetsPendientesAsync(CancellationToken.None)));
        Assert.Equal("wa-8095550101", pendiente.SesionId);
        Assert.Contains("¿Correcto?", Assert.Single(api.Textos).Texto);
    }

    [Fact]
    public async Task Tipos_no_soportados_reciben_una_explicacion()
    {
        var modelo = new ScriptedChatModel();
        var (app, api) = Crear(modelo);
        using var _app = app;

        await EnviarWebhook(app, Payloads.Sticker("PNID", "wamid.6"));
        await Procesar(app);

        Assert.Empty(modelo.Llamadas);
        Assert.Equal(ProcesadorWhatsApp.SoloTextoVozYCarnet, Assert.Single(api.Textos).Texto);
    }

    [Fact]
    public async Task Si_falla_varias_veces_se_disculpa_y_escala_al_personal()
    {
        var modelo = new ScriptedChatModel();
        for (var i = 0; i < 3; i++)
            modelo.Then(_ => throw new HttpRequestException("Meta no respondió"));
        var (app, api) = Crear(modelo);
        using var _app = app;

        await EnviarWebhook(app, Payloads.Texto("PNID", "wamid.7", "hola"));
        await Procesar(app);
        await Procesar(app);
        Assert.Empty(api.Textos);
        await Procesar(app);

        var entrante = Assert.Single(await Entrantes(app));
        Assert.Equal("fallido", entrante.Estado);
        Assert.Equal(3, entrante.Intentos);
        Assert.Equal(ProcesadorWhatsApp.Disculpa, Assert.Single(api.Textos).Texto);
        Assert.Single(await app.ConGateway(g => g.ListarEscalamientosAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task El_recordatorio_sale_por_whatsapp_con_su_plantilla()
    {
        var api = new WhatsAppApiFalsa();
        var reloj = new RelojAjustable(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4)));
        using var app = new AppDePrueba(null, s =>
        {
            s.RemoveAll<IWhatsAppCloudApi>();
            s.AddSingleton<IWhatsAppCloudApi>(api);
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
        }, new Dictionary<string, string?>
        {
            ["WhatsApp:PhoneNumberId"] = "PNID",
            ["WhatsApp:ProcesadorActivo"] = "false",
            ["WhatsApp:Plantillas:recordatorio"] = "recordatorio_cita",
        });

        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01",
            HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)), "control", CancellationToken.None));
        reloj.Avanzar(TimeSpan.FromHours(7));
        await app.Services.GetRequiredService<AgendaHostedService>().VueltaAsync(CancellationToken.None);

        var enviado = Assert.Single(api.Plantillas);
        Assert.Equal("recordatorio_cita", enviado.Plantilla);
        Assert.Equal("18095550101", enviado.Destino);
        Assert.Equal("María", enviado.Parametros[0]);
    }
}

/// <summary>Cuerpos de webhook con la forma que envía WhatsApp Cloud API.</summary>
public static class Payloads
{
    private static string Envolver(string phoneNumberId, string mensajes) => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp",
          "metadata":{"display_phone_number":"18095550000","phone_number_id":"{{{phoneNumberId}}}"},
          "contacts":[{"profile":{"name":"María"},"wa_id":"18095550101"}],
          "messages":[{{{mensajes}}}]}}]}]}
        """;

    public static string Texto(string pnid, string id, string texto) =>
        Envolver(pnid, $$$"""{"from":"18095550101","id":"{{{id}}}","timestamp":"1761494400","type":"text","text":{"body":"{{{texto}}}"}}""");

    public static string Audio(string pnid, string id, string media) =>
        Envolver(pnid, $$$"""{"from":"18095550101","id":"{{{id}}}","timestamp":"1761494400","type":"audio","audio":{"id":"{{{media}}}","mime_type":"audio/ogg; codecs=opus","voice":true}}""");

    public static string Imagen(string pnid, string id, string media) =>
        Envolver(pnid, $$$"""{"from":"18095550101","id":"{{{id}}}","timestamp":"1761494400","type":"image","image":{"id":"{{{media}}}","mime_type":"image/jpeg"}}""");

    public static string Sticker(string pnid, string id) =>
        Envolver(pnid, $$$"""{"from":"18095550101","id":"{{{id}}}","timestamp":"1761494400","type":"sticker","sticker":{"id":"S1"}}""");

    public static string Varios(string pnid) => Envolver(pnid, string.Join(",",
        """{"from":"18095550101","id":"w1","type":"text","text":{"body":"Quiero una cita"}}""",
        """{"from":"18095550101","id":"w2","type":"audio","audio":{"id":"MEDIA-AUDIO"}}""",
        """{"from":"18095550101","id":"w3","type":"image","image":{"id":"MEDIA-IMG"}}""",
        """{"from":"18095550101","id":"w4","type":"button","button":{"text":"CONFIRMO","payload":"ok"}}""",
        """{"from":"18095550101","id":"w5","type":"location","location":{"latitude":18.4}}"""));

    public static string Estado(string pnid) => $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"messages","value":{
          "messaging_product":"whatsapp","metadata":{"phone_number_id":"{{{pnid}}}"},
          "statuses":[{"id":"wamid.x","status":"delivered","recipient_id":"18095550101"}]}}]}]}
        """;
}
