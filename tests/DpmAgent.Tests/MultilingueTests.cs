using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Canales.WhatsApp;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Idiomas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

public class DetectorIdiomaTests
{
    [Theory]
    [InlineData("Hola, quiero una cita con la doctora para mañana", "es")]
    [InlineData("¿Tienen turno el martes en la tarde?", "es")]
    [InlineData("Hello, I need an appointment with the doctor tomorrow morning", "en")]
    [InlineData("Can I book something for Tuesday?", "en")]
    [InlineData("Bonjou, mwen vle pran yon randevou ak doktè a demen", "ht")]
    [InlineData("Mwen bezwen anile randevou mwen an", "ht")]
    [InlineData("Bonjour, je voudrais un rendez-vous avec le médecin demain", "fr")]
    [InlineData("Olá, quero marcar uma consulta para amanhã", "pt")]
    public void Detecta_el_idioma_del_mensaje(string texto, string esperado)
    {
        Assert.Equal(esperado, DetectorIdioma.Detectar(texto)?.Codigo);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("9:00")]
    [InlineData("CITA-12")]
    [InlineData("")]
    [InlineData("no")]
    public void Un_mensaje_ambiguo_no_decide_idioma(string texto)
    {
        Assert.Null(DetectorIdioma.Detectar(texto));
    }

    [Theory]
    [InlineData("sí", "es")]
    [InlineData("yes", "en")]
    [InlineData("wi, mèsi", "ht")]
    [InlineData("oui merci", "fr")]
    public void Respuestas_cortas_con_palabras_propias_si_deciden(string texto, string esperado)
    {
        Assert.Equal(esperado, DetectorIdioma.Detectar(texto)?.Codigo);
    }

    [Fact]
    public void Niveles_y_respaldo()
    {
        Assert.Equal(1, Idiomas.Nivel("es-DO"));
        Assert.Equal(1, Idiomas.Nivel("ht"));
        Assert.Equal(2, Idiomas.Nivel("fr"));
        Assert.Equal("en", Idiomas.ParaTextos("pt"));
        Assert.Equal("ht", Idiomas.ParaTextos("ht"));
        Assert.Equal("es", Idiomas.ParaTextos(null));
    }
}

public class TextosTests
{
    [Fact]
    public void Los_tres_idiomas_tienen_las_mismas_claves_y_marcadores()
    {
        var es = Textos.Claves("es").Order().ToList();

        foreach (var idioma in new[] { "en", "ht" })
        {
            Assert.Equal(es, Textos.Claves(idioma).Order().ToList());
            foreach (var clave in es)
            {
                // Mismos {0}, {1}... en cada traducción.
                var marcas = (string i) => System.Text.RegularExpressions.Regex.Matches(Textos.Obtener(clave, i), @"\{\d\}")
                    .Select(m => m.Value).Order().ToList();
                Assert.Equal(marcas("es"), marcas(idioma));
            }
        }
    }

    [Fact]
    public void Nivel_2_usa_ingles_y_formatea_fechas_por_idioma()
    {
        var martes9 = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));

        Assert.Equal(Textos.Obtener("canal.disculpa", "en"), Textos.Obtener("canal.disculpa", "fr"));
        Assert.Equal("martes 27 de octubre", Textos.Dia(martes9, "es"));
        Assert.Equal("Tuesday, October 27", Textos.Dia(martes9, "en"));
        Assert.Equal("madi 27 oktòb", Textos.Dia(martes9, "ht"));
        Assert.Equal("9:00 AM", Textos.Hora(martes9, "en"));
        Assert.Equal("La nota de voz dura más de 120 segundos.", Textos.Obtener("entrada.audio_largo", "es", "120"));
        Assert.Equal("Nòt vokal la pi long pase 120 segonn.", Textos.Obtener("entrada.audio_largo", "ht", "120"));
    }

    [Fact]
    public void Los_mensajes_automaticos_salen_en_el_idioma_pedido()
    {
        var martes9 = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));

        var es = TextosAgenda.Recordatorio("es", "Consultorio Ejemplo (ficticio)", "María Rodríguez", "Dra. Ana", martes9, "CITA-1");
        var en = TextosAgenda.Recordatorio("en", "Consultorio Ejemplo (ficticio)", "María Rodríguez", "Dra. Ana", martes9, "CITA-1");
        var ht = TextosAgenda.Recordatorio("ht", "Consultorio Ejemplo (ficticio)", "María Rodríguez", "Dra. Ana", martes9, "CITA-1");
        var fr = TextosAgenda.Recordatorio("fr", "Consultorio Ejemplo (ficticio)", "María Rodríguez", "Dra. Ana", martes9, "CITA-1");

        Assert.Contains("CONFIRMO", es.Texto);
        Assert.Contains("Tuesday, October 27", en.Texto);
        Assert.Contains("KONFIME", ht.Texto);
        Assert.Contains("madi 27 oktòb", ht.Texto);
        Assert.Equal(en.Texto, fr.Texto);
        Assert.Equal("en", fr.Idioma);
        Assert.Equal(["es", "en", "ht", "en"], new[] { es, en, ht, fr }.Select(m => m.Idioma));
        // Plantillas de utilidad: nombran al consultorio y dicen cómo darse de baja, en cada idioma.
        Assert.All(new[] { es, en, ht }, m =>
        {
            Assert.Contains("Consultorio Ejemplo (ficticio)", m.Texto);
            Assert.Contains("STOP", m.Texto);
            Assert.Equal("Consultorio Ejemplo (ficticio)", m.Parametros[0]);
        });
    }
}

/// <summary>Idioma en la sesión, en el agente y en los mensajes, con el modelo falso.</summary>
public class AgenteMultilingueTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private readonly MockDpmGateway _dpm = new(new FixedClock(Ahora));

    private ReceptionAgent Agente(ScriptedChatModel modelo, IConversationStore? store = null) =>
        new(modelo, new AgentTools(_dpm), store ?? new InMemoryConversationStore(), NullLogger<ReceptionAgent>.Instance, new FixedClock(Ahora));

    private static string Sistema(ScriptedChatModel modelo, int llamada) => modelo.Llamadas[llamada][0]["content"]!.ToString();

    [Fact]
    public async Task Sigue_el_idioma_del_paciente_y_lo_cambia_si_el_cambia()
    {
        var modelo = new ScriptedChatModel().ThenText("Hola").ThenText("Perfecto").ThenText("Of course").ThenText("Bon");
        var agente = Agente(modelo);

        var r1 = await agente.RunAsync(null, "Hola, quiero una cita", "8095550101", CancellationToken.None);
        var r2 = await agente.RunAsync(r1.SesionId, "ok", null, CancellationToken.None);
        var r3 = await agente.RunAsync(r1.SesionId, "Actually, can I speak English please?", null, CancellationToken.None);
        var r4 = await agente.RunAsync(r1.SesionId, "Mwen pito pale kreyòl, mèsi", null, CancellationToken.None);

        Assert.Equal(["es", "es", "en", "ht"], new[] { r1, r2, r3, r4 }.Select(r => r.Idioma));
        Assert.Contains("(código es)", Sistema(modelo, 1));
        Assert.Contains("(código en)", Sistema(modelo, 2));
        Assert.Contains("Kreyòl ayisyen (código ht)", Sistema(modelo, 3));
    }

    [Fact]
    public async Task El_primer_saludo_ambiguo_usa_el_idioma_del_consultorio()
    {
        _dpm.Consultorio = new InfoConsultorio("Clinic (fictional)", "911", "en");
        var modelo = new ScriptedChatModel().ThenText("Hi!");

        var r = await Agente(modelo).RunAsync(null, "👋", "8095550101", CancellationToken.None);

        Assert.Equal("en", r.Idioma);
    }

    [Theory]
    [InlineData("I have chest pain and I can't breathe", "en")]
    [InlineData("Mwen gen yon gwo doulè nan pwatrin mwen", "ht")]
    [InlineData("J'ai une forte douleur à la poitrine", "fr")]
    public async Task Las_reglas_de_seguridad_y_el_numero_de_emergencias_valen_en_cualquier_idioma(string mensaje, string idioma)
    {
        _dpm.Consultorio = new InfoConsultorio("Consultorio (ficticio)", "112", "es");
        var modelo = new ScriptedChatModel().ThenText("...");

        var r = await Agente(modelo).RunAsync(null, mensaje, "8095550101", CancellationToken.None);
        var sistema = Sistema(modelo, 0);

        Assert.Equal(idioma, r.Idioma);
        Assert.Contains("Nunca des orientación médica", sistema);
        Assert.Contains("llame al 112", sistema);
        Assert.Contains("valen igual en cualquier idioma", sistema);
        Assert.DoesNotContain("911", sistema);
    }

    [Fact]
    public async Task En_nivel_2_pide_aclarar_o_escalar_si_no_entiende()
    {
        var modelo = new ScriptedChatModel().ThenText("D'accord");

        await Agente(modelo).RunAsync(null, "Bonjour, je voudrais un rendez-vous demain", "8095550101", CancellationToken.None);

        Assert.Contains("no es uno de los idiomas principales", Sistema(modelo, 0));
        Assert.Contains("pídele que lo aclare", Sistema(modelo, 0));
    }

    [Fact]
    public async Task Los_textos_fijos_del_agente_salen_en_el_idioma_del_paciente()
    {
        var modelo = new ScriptedChatModel();
        for (var i = 0; i < 10; i++)
            modelo.ThenTool("listar_medicos", new { });

        var r = await Agente(modelo).RunAsync(null, "Bonjou, mwen bezwen yon randevou", "8095550101", CancellationToken.None);

        Assert.EndsWith(Textos.Obtener("agente.limite_iteraciones", "ht"), r.Respuesta);
    }

    [Fact]
    public async Task El_escalamiento_guarda_el_mensaje_original_y_resumenes_en_es_y_en()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("escalar_a_personal", new { resumen_es = "Pregunta por dosis de medicamento", resumen_en = "Asks about medication dose", urgente = false })
            .ThenText("Se pèsonèl medikal la ki pou reponn sa.");
        var r = await Agente(modelo).RunAsync(null, "Konbyen grenn mwen dwe bay pitit mwen an?", "8095550101", CancellationToken.None);

        var e = Assert.Single(await _dpm.ListarEscalamientosAsync(CancellationToken.None));
        Assert.Equal("ht", r.Idioma);
        Assert.Equal("Konbyen grenn mwen dwe bay pitit mwen an?", e.MensajeOriginal);
        Assert.Equal("Pregunta por dosis de medicamento", e.Motivo);
        Assert.Equal("Asks about medication dose", e.MotivoEn);
        Assert.Equal("ht", e.Idioma);
    }

    [Fact]
    public async Task Si_el_modelo_omite_un_resumen_el_escalamiento_igual_se_crea()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("escalar_a_personal", new { resumen_en = "Asks for a diagnosis", urgente = false })
            .ThenText("The doctor will answer that.");

        await Agente(modelo).RunAsync(null, "What do I have, doctor?", "8095550101", CancellationToken.None);

        var e = Assert.Single(await _dpm.ListarEscalamientosAsync(CancellationToken.None));
        Assert.Equal("Asks for a diagnosis", e.Motivo);
        Assert.Equal("Asks for a diagnosis", e.MotivoEn);
    }

    public static TheoryData<string, string[], string> Conversaciones => new()
    {
        { "es", ["Hola, quiero una cita con la doctora el martes a las 9", "Sí, confirme por favor"], "Su cita quedó confirmada." },
        { "en", ["Hi, I would like an appointment with the doctor on Tuesday at 9", "Yes, please book it"], "Your appointment is confirmed." },
        { "ht", ["Bonjou, mwen vle pran yon randevou ak doktè a madi a 9è", "Wi, tanpri konfime l"], "Randevou ou konfime." },
    };

    [Theory]
    [MemberData(nameof(Conversaciones))]
    public async Task Conversacion_completa_de_agendar(string idioma, string[] mensajes, string final)
    {
        var slot = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)).ToString("yyyy-MM-dd'T'HH:mm:sszzz");
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("consultar_disponibilidad", new { medico_id = "MED-01", fecha = "2026-10-27" })
            .ThenText("...?")
            .ThenTool("crear_cita", new { medico_id = "MED-01", fecha_hora = slot, motivo = "consulta general" })
            .ThenText(final);
        var agente = Agente(modelo);

        var r1 = await agente.RunAsync(null, mensajes[0], "8095550101", CancellationToken.None);
        var r2 = await agente.RunAsync(r1.SesionId, mensajes[1], null, CancellationToken.None);

        Assert.Equal(idioma, r1.Idioma);
        Assert.Equal(idioma, r2.Idioma);
        Assert.Contains("\"creada\":true", r2.Pasos[0].Resultado);
        Assert.Equal(final, r2.Respuesta);
        Assert.Contains($"(código {idioma})", Sistema(modelo, 3));
        Assert.Single(await _dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None));
    }
}

/// <summary>Idioma guardado en la base, plantillas de WhatsApp y textos de error por HTTP.</summary>
public class IdiomaPersistenteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public async Task El_idioma_se_guarda_en_la_conversacion_y_en_el_paciente()
    {
        var reloj = new FixedClock(Ahora);
        using var db = new SqliteDePrueba(reloj);
        var modelo = new ScriptedChatModel().ThenTool("buscar_paciente", new { }).ThenText("Bonjou").ThenText("Dakò");
        ReceptionAgent Agente() => new(modelo, new AgentTools(new DbDpmGateway(db.Contexto(), reloj)),
            new DbConversationStore(db.Contexto(), reloj), NullLogger<ReceptionAgent>.Instance, reloj);

        var r1 = await Agente().RunAsync("wa-8095550101", "Bonjou, mwen vle yon randevou", "8095550101", CancellationToken.None);
        var r2 = await Agente().RunAsync(r1.SesionId, "ok", null, CancellationToken.None);

        var ctx = db.Contexto();
        var conv = await ctx.Conversaciones.SingleAsync();
        Assert.Equal("ht", r2.Idioma);
        Assert.Equal("ht", conv.Idioma);
        Assert.Equal(1, conv.NivelIdioma);
        Assert.Equal("ht", (await ctx.Pacientes.SingleAsync(p => p.Id == 1)).Idioma);
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData("en", "en")]
    [InlineData("ht", "ht")]
    [InlineData("fr", "en")]
    [InlineData(null, "es")]
    public async Task La_plantilla_de_whatsapp_va_en_el_idioma_del_mensaje(string? idioma, string codigoPlantilla)
    {
        var api = new WhatsAppApiFalsa();
        var canal = new WhatsAppCloudChannel(api, Options.Create(new WhatsAppOptions
        {
            PhoneNumberId = "PNID",
            Plantillas = new() { ["recordatorio"] = "recordatorio_cita" },
        }));

        await canal.EnviarAsync(new MensajeParaEnviar(1, "wa-8095550101", "8095550101", "texto", "recordatorio",
            null, "recordatorio", ["a", "b", "c", "d", "e"], idioma), CancellationToken.None);

        Assert.Equal(codigoPlantilla, Assert.Single(api.Plantillas).Idioma);
    }

    [Fact]
    public async Task Los_mensajes_automaticos_usan_el_idioma_del_paciente_o_ingles_en_nivel_2()
    {
        var reloj = new RelojAjustable(Ahora);
        using var db = new SqliteDePrueba(reloj);
        var ctx = db.Contexto();
        (await ctx.Pacientes.SingleAsync(p => p.Id == 1)).Idioma = "ht";
        (await ctx.Pacientes.SingleAsync(p => p.Id == 2)).Idioma = "pt";
        await ctx.SaveChangesAsync();

        var gw = new DbDpmGateway(db.Contexto(), reloj);
        await gw.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)), "control", CancellationToken.None);
        await gw.CrearCitaAsync("PAC-002", "MED-01", HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(10, 0)), "control", CancellationToken.None);
        reloj.Avanzar(TimeSpan.FromHours(7));

        var c = db.Contexto();
        var motor = new MotorAgenda(c, new Notificador(c, new DbConversationStore(c, reloj), new DespertadorAgenda(), reloj),
            new Bitacora(c, reloj), Options.Create(new AgendaOptions()), reloj, NullLogger<MotorAgenda>.Instance);
        await motor.ProcesarAsync(CancellationToken.None);

        var mensajes = await db.Contexto().MensajesSalientes.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(["ht", "en"], mensajes.Select(m => m.Idioma));
        Assert.Contains("KONFIME", mensajes[0].Texto);
        Assert.Contains("Reply CONFIRM", mensajes[1].Texto);
    }

    [Fact]
    public async Task Los_errores_por_http_salen_en_el_idioma_de_la_interfaz()
    {
        using var app = new AppDePrueba();

        var en = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = " ", idioma = "en" });
        var porEncabezado = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(new { mensaje = "" }) };
        porEncabezado.Headers.AcceptLanguage.ParseAdd("ht-HT");
        var ht = await app.Cliente.SendAsync(porEncabezado);

        Assert.Equal("The message is empty.", (await en.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.ToString());
        Assert.Equal("Mesaj la vid.", (await ht.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.ToString());
    }

    [Fact]
    public async Task Whatsapp_explica_en_kreyol_si_el_paciente_escribe_en_kreyol()
    {
        var api = new WhatsAppApiFalsa();
        var modelo = new ScriptedChatModel().ThenText("Bonjou!");
        using var app = new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<IWhatsAppCloudApi>();
            s.AddSingleton<IWhatsAppCloudApi>(api);
        }, new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = "s",
            ["WhatsApp:PhoneNumberId"] = "PNID",
        });

        async Task Enviar(string json)
        {
            var cuerpo = System.Text.Encoding.UTF8.GetBytes(json);
            var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = new ByteArrayContent(cuerpo) };
            peticion.Headers.Add("X-Hub-Signature-256", WebhookWhatsApp.Firmar(cuerpo, "s"));
            await app.Cliente.SendAsync(peticion);
            await app.Services.GetRequiredService<ProcesadorWhatsApp>().ProcesarPendientesAsync(CancellationToken.None);
            await app.EnAmbito(sp => sp.GetRequiredService<DespachadorMensajes>().DespacharPendientesAsync(CancellationToken.None));
        }

        await Enviar(Payloads.Texto("PNID", "w1", "Bonjou, mwen vle yon randevou"));
        await Enviar(Payloads.Sticker("PNID", "w2"));

        Assert.Equal(Textos.Obtener("canal.solo_texto_voz_carnet", "ht"), api.Textos[1].Texto);
    }
}

public class ConfiguracionConsultorioTests
{
    [Fact]
    public async Task El_admin_configura_idioma_y_numero_de_emergencias_y_el_agente_los_usa()
    {
        var modelo = new ScriptedChatModel().ThenText("Hi!");
        using var app = new AppDePrueba(modelo);
        await app.IniciarSesionAsync();

        var malIdioma = await app.Cliente.PutAsJsonAsync("/api/panel/consultorio", new { idiomaPredeterminado = "fr" });
        var malTelefono = await app.Cliente.PutAsJsonAsync("/api/panel/consultorio", new { telefonoEmergencias = "9-1-1" });
        var bien = await app.Cliente.PutAsJsonAsync("/api/panel/consultorio", new { idiomaPredeterminado = "en", telefonoEmergencias = "112" });
        var chat = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "👋", telefono = "8095550101" });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, malIdioma.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, malTelefono.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, bien.StatusCode);
        Assert.Equal("en", (await chat.Content.ReadFromJsonAsync<JsonObject>())!["idioma"]!.ToString());
        Assert.Contains("llame al 112", modelo.Llamadas[0][0]["content"]!.ToString());
    }
}
