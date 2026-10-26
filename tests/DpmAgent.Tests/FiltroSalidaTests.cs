using System.Net;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Idiomas;
using DpmAgent.Api.Model;
using DpmAgent.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

public class FiltroReglasTests
{
    private static readonly FiltroReglas Reglas = new();

    [Theory]
    [InlineData("Dele 5 ml de ibuprofeno cada 8 horas.")]
    [InlineData("Puede tomar 500 mg de paracetamol.")]
    [InlineData("Give him 10 mg twice a day.")]
    [InlineData("Take ibuprofen with food.")]
    [InlineData("Ba li 5 ml ibipwofèn chak 8 è.")]
    [InlineData("Pran 2 grenn parasetamòl.")]
    [InlineData("Donnez-lui du paracétamol toutes les 6 heures.")]
    [InlineData("Dê amoxicilina para a sua filha.")]
    [InlineData("Tómese dos pastillas... mejor 2 tabletas")]
    public async Task Bloquea_dosis_y_medicamentos_en_varios_idiomas(string respuesta)
    {
        Assert.True((await Reglas.RevisarAsync(respuesta, null, CancellationToken.None)).Bloquear);
    }

    [Theory]
    [InlineData("Su cita quedó para el martes 27 a las 9:00 a. m. con la Dra. Ana Valdez.")]
    [InlineData("Tengo 3 horarios: 8:00, 8:30 y 9:00. ¿Cuál prefiere?")]
    [InlineData("Llame al 911 ahora mismo o vaya a la emergencia más cercana.")]
    [InlineData("No puedo indicar medicamentos ni dosis; eso lo decide el médico.")]
    [InlineData("Your appointment CITA-12 is confirmed. Reply CONFIRM.")]
    [InlineData("Ou gen yon randevou madi 27 a 9:00 AM ak Dr. Ana Valdez.")]
    [InlineData("La oferta vence en 30 minutos.")]
    public async Task No_bloquea_respuestas_administrativas(string respuesta)
    {
        Assert.False((await Reglas.RevisarAsync(respuesta, null, CancellationToken.None)).Bloquear);
    }
}

public class ClasificadoresTests
{
    private sealed class Handler(string contenido, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public JsonObject? Enviado { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Enviado = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            var r = new JsonObject
            {
                ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = contenido } }),
                ["usage"] = new JsonObject { ["prompt_tokens"] = 120, ["completion_tokens"] = 3 },
            };
            return new HttpResponseMessage(status) { Content = new StringContent(r.ToJsonString()) };
        }
    }

    private static FiltroLlamaGuard Guard(Handler h, IRegistroUso? registro = null) => new(new HttpClient(h),
        Options.Create(new FiltroSalidaOptions { BaseUrl = "http://guard.local/v1", Model = "llama-guard", ApiKey = "k" }), registro);

    [Fact]
    public async Task Llama_guard_recibe_el_turno_y_bloquea_s6()
    {
        var h = new Handler("unsafe\nS6");
        var uso = new RegistroUsoEnMemoria();

        var v = await Guard(h, uso).RevisarAsync("Give him 5 ml every 8 hours", "How much ibuprofen?", CancellationToken.None);

        Assert.True(v.Bloquear);
        Assert.Equal("llama-guard", v.Fuente);
        Assert.Contains("S6", v.Motivo);
        Assert.Equal("user", h.Enviado!["messages"]![0]!["role"]!.ToString());
        Assert.Equal("assistant", h.Enviado["messages"]![1]!["role"]!.ToString());
        Assert.Equal("filtro", Assert.Single(uso.Usos).Proposito);
    }

    [Fact]
    public async Task Llama_guard_deja_pasar_safe_y_falla_con_salidas_raras()
    {
        Assert.False((await Guard(new Handler("safe")).RevisarAsync("Your appointment is at 9", "hi", CancellationToken.None)).Bloquear);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Guard(new Handler("no sé")).RevisarAsync("x", "y", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => Guard(new Handler("safe", HttpStatusCode.BadGateway)).RevisarAsync("x", "y", CancellationToken.None));
    }

    [Fact]
    public async Task El_clasificador_con_el_modelo_lee_json_y_registra_el_proposito()
    {
        var modelo = new ScriptedChatModel()
            .ThenText("""{"consejo_medico": true, "motivo": "interpreta un resultado"}""")
            .ThenText("""{"consejo_medico": false, "motivo": ""}""")
            .ThenText("no es json");
        var filtro = new FiltroModelo(modelo);

        var bloquea = await filtro.RevisarAsync("180 es alto, debe preocuparse", "glucosa 180?", CancellationToken.None);
        var pasa = await filtro.RevisarAsync("Su cita es el martes", "cita", CancellationToken.None);

        Assert.True(bloquea.Bloquear);
        Assert.Contains("interpreta un resultado", bloquea.Motivo);
        Assert.False(pasa.Bloquear);
        Assert.Contains("RESPUESTA DEL ASISTENTE:\n180 es alto", modelo.Llamadas[0][1]["content"]!.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => filtro.RevisarAsync("x", "y", CancellationToken.None));
    }

    private sealed class ClasificadorQueFalla : IFiltroSalida
    {
        public int Llamadas { get; private set; }

        public Task<VeredictoFiltro> RevisarAsync(string respuesta, string? mensajePaciente, CancellationToken ct)
        {
            Llamadas++;
            throw new HttpRequestException("caído");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Si_el_clasificador_falla_se_aplica_la_configuracion(bool bloquearSiFalla, bool bloquea)
    {
        var clasificador = new ClasificadorQueFalla();
        var compuesto = new FiltroCompuesto(new FiltroReglas(), clasificador,
            Options.Create(new FiltroSalidaOptions { BloquearSiFalla = bloquearSiFalla }), NullLogger<FiltroCompuesto>.Instance);

        var v = await compuesto.RevisarAsync("Su cita es el martes a las 9", "cita", CancellationToken.None);
        var porReglas = await compuesto.RevisarAsync("Tome 500 mg", "dolor", CancellationToken.None);

        Assert.Equal(bloquea, v.Bloquear);
        Assert.True(porReglas.Bloquear);
        Assert.Equal("reglas", porReglas.Fuente);
        Assert.Equal(1, clasificador.Llamadas); // si las reglas ya bloquean, no se llama al clasificador
    }

    [Theory]
    [InlineData("Reglas")]
    [InlineData("Modelo")]
    [InlineData("LlamaGuard")]
    public void Se_registra_segun_la_configuracion(string proveedor)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FiltroSalida:Proveedor"] = proveedor,
            ["FiltroSalida:BaseUrl"] = "http://guard.local/v1/",
            ["FiltroSalida:Model"] = "llama-guard",
        }).Build();
        var s = new ServiceCollection().AddLogging();
        s.AddSingleton<IChatModel>(new ScriptedChatModel());
        s.AddFiltroSalida(config);

        using var sp = s.BuildServiceProvider();
        Assert.IsType<FiltroCompuesto>(sp.CreateScope().ServiceProvider.GetRequiredService<IFiltroSalida>());
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddFiltroSalida(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FiltroSalida:Proveedor"] = "Otro" }).Build()));
    }
}

/// <summary>El filtro dentro del agente: reemplaza, escala y deja rastro.</summary>
public class FiltroEnElAgenteTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    [Theory]
    [InlineData("¿Cuánto ibuprofeno le doy a mi hijo?", "Dele 5 ml cada 8 horas.", "es")]
    [InlineData("Konbyen ibipwofèn mwen ka bay pitit mwen?", "Ba li 5 ml chak 8 è.", "ht")]
    [InlineData("Quanto paracetamol posso dar ao meu filho?", "Dê 5 ml a cada 8 horas.", "en")]
    public async Task Una_respuesta_con_dosis_se_reemplaza_se_escala_y_queda_en_la_bitacora(string pregunta, string peligrosa, string idiomaSeguro)
    {
        var reloj = new FixedClock(Ahora);
        using var db = new SqliteDePrueba(reloj);
        var ctx = db.Contexto();
        var dpm = new DbDpmGateway(ctx, reloj);
        var bitacora = new Bitacora(ctx, reloj);
        var modelo = new ScriptedChatModel().ThenText(peligrosa).ThenText("¿Algo más?");
        var store = new DbConversationStore(ctx, reloj);
        var agente = new ReceptionAgent(modelo, new AgentTools(dpm, null, bitacora), store, NullLogger<ReceptionAgent>.Instance, reloj,
            filtro: new FiltroCompuesto(new FiltroReglas(), null, Options.Create(new FiltroSalidaOptions()), NullLogger<FiltroCompuesto>.Instance));

        var r = await agente.RunAsync("wa-8095550101", pregunta, "8095550101", CancellationToken.None);
        await agente.RunAsync(r.SesionId, "ok", null, CancellationToken.None);

        Assert.Equal(Textos.Obtener("agente.respuesta_bloqueada", idiomaSeguro, "911"), r.Respuesta);
        Assert.Contains(r.Pasos, p => p.Herramienta == "filtro_salida" && p.Resultado.Contains("\"bloqueada\":true"));

        var e = Assert.Single(await dpm.ListarEscalamientosAsync(CancellationToken.None));
        Assert.Equal(pregunta, e.MensajeOriginal);
        Assert.Contains("filtro de seguridad", e.Motivo);

        var registro = await db.Contexto().Bitacora.SingleAsync(b => b.Accion == "respuesta_bloqueada");
        Assert.Equal(peligrosa, registro.TextoOriginal);
        Assert.StartsWith("reglas:", registro.Detalle);

        // En el siguiente turno el modelo ve el texto seguro, nunca la respuesta bloqueada.
        var historial = string.Join("\n", modelo.Llamadas[1].Select(m => m["content"]?.ToString()));
        Assert.DoesNotContain(peligrosa, historial);
    }
}
