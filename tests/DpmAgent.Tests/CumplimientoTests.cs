using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

public class PedidosDirectosTests
{
    [Theory]
    [InlineData("STOP")]
    [InlineData("stop.")]
    [InlineData("Baja")]
    [InlineData("Unsubscribe")]
    [InlineData("Sispann")]
    [InlineData("darme de baja")]
    public void Reconoce_la_baja(string mensaje) => Assert.True(PedidosDirectos.EsBaja(mensaje));

    [Theory]
    [InlineData("Quiero cancelar mi cita")]
    [InlineData("stop sending me reminders about the flu shot please")]
    [InlineData("¿Puedo bajar la cita a las 9?")]
    public void No_confunde_otros_mensajes_con_la_baja(string mensaje) => Assert.False(PedidosDirectos.EsBaja(mensaje));

    [Theory]
    [InlineData("Quiero hablar con una persona")]
    [InlineData("Prefiero hablar con la secretaria")]
    [InlineData("can I talk to a real person?")]
    [InlineData("I want to speak with someone")]
    [InlineData("Mwen vle pale ak yon moun")]
    [InlineData("Je voudrais parler à une personne")]
    [InlineData("Quero falar com uma pessoa")]
    public void Reconoce_el_pedido_de_persona(string mensaje) => Assert.True(PedidosDirectos.EsPedidoDePersona(mensaje));

    [Theory]
    [InlineData("Quiero una cita para una persona mayor")]
    [InlineData("My daughter is the person who needs the appointment")]
    public void No_confunde_citas_con_pedidos_de_persona(string mensaje) => Assert.False(PedidosDirectos.EsPedidoDePersona(mensaje));
}

public class CumplimientoTests
{
    private static readonly DateTimeOffset Lunes = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private static (AppDePrueba App, RelojAjustable Reloj) Crear(ScriptedChatModel? modelo = null)
    {
        var reloj = new RelojAjustable(Lunes);
        var app = new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
        });
        return (app, reloj);
    }

    private static Task<T> ConDb<T>(AppDePrueba app, Func<DpmDbContext, Task<T>> accion) =>
        app.EnAmbito(sp => accion(sp.GetRequiredService<DpmDbContext>()));

    private static Task SinConsentimientos(AppDePrueba app) => ConDb(app, db => db.Consentimientos.ExecuteDeleteAsync());

    private static async Task<List<(string Tipo, string Estado)>> AvisosAsync(AppDePrueba app) =>
        (await ConDb(app, db => db.MensajesSalientes.Where(m => m.Tipo != "respuesta").Select(m => new { m.Tipo, m.Estado }).ToListAsync()))
        .Select(m => (m.Tipo, m.Estado)).ToList();

    private static async Task RecordatorioDeMariaAsync(AppDePrueba app, RelojAjustable reloj)
    {
        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01",
            HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)), "control", CancellationToken.None));
        reloj.Avanzar(TimeSpan.FromHours(7));
        await app.Services.GetRequiredService<AgendaHostedService>().VueltaAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Con_permiso_sale_el_recordatorio()
    {
        var (app, reloj) = Crear();
        using var _ = app;

        await RecordatorioDeMariaAsync(app, reloj);

        Assert.Equal([("recordatorio", "pendiente")], await AvisosAsync(app)); // en la cola de salida
    }

    [Fact]
    public async Task Sin_permiso_no_sale_y_queda_para_que_la_secretaria_llame()
    {
        var (app, reloj) = Crear();
        using var _ = app;
        await SinConsentimientos(app);

        await RecordatorioDeMariaAsync(app, reloj);

        Assert.Equal([("recordatorio", Notificador.EstadoLlamar)], await AvisosAsync(app));
        var historial = await ConDb(app, db => db.Mensajes.CountAsync());
        Assert.Equal(0, historial);

        await app.IniciarSesionAsync();
        var llamadas = (await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/llamadas"))!;
        var llamada = Assert.Single(llamadas)!;
        Assert.Equal("María Rodríguez (ficticia)", llamada["paciente"]!.ToString());
        Assert.Equal("recordatorio", llamada["tipo"]!.ToString());

        var hecha = await app.Cliente.PostAsync($"/api/panel/llamadas/{llamada["id"]}/hecha", null);
        Assert.Equal(HttpStatusCode.OK, hecha.StatusCode);
        Assert.Empty((await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/llamadas"))!);
    }

    [Fact]
    public async Task La_baja_por_stop_detiene_los_avisos_y_volver_requiere_nuevo_permiso()
    {
        var (app, reloj) = Crear(new ScriptedChatModel());
        using var _ = app;

        var r = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "STOP", telefono = "8095550101" });
        var json = (await r.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("no le enviaremos más avisos", json["respuesta"]!.ToString());
        Assert.Empty(app.Modelo.Llamadas);

        await RecordatorioDeMariaAsync(app, reloj);
        Assert.Equal([("recordatorio", Notificador.EstadoLlamar)], await AvisosAsync(app));

        var estado = await app.EnAmbito(sp => sp.GetRequiredService<ServicioConsentimiento>().EstadoAsync(1, CancellationToken.None));
        Assert.All(estado, e => Assert.False(e.Otorgado));
        Assert.All(estado, e => Assert.Equal("web", e.Canal));
    }

    [Fact]
    public async Task El_permiso_se_registra_solo_con_la_respuesta_del_paciente_en_otro_turno()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("ofrecer_consentimiento", new { })
            .ThenTool("registrar_consentimiento", new { acepta = true }) // mismo turno: se rechaza
            .ThenText("Abajo está el aviso de permiso.")
            .ThenTool("registrar_consentimiento", new { acepta = true })
            .ThenText("Perfecto.");
        var (app, _) = Crear(modelo);
        using var __ = app;
        await SinConsentimientos(app);

        var r1 = (await (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Quiero recordatorios", telefono = "8095550101" }))
            .Content.ReadFromJsonAsync<JsonObject>())!;
        var texto1 = r1["respuesta"]!.ToString();
        Assert.Contains("Consultorio Ejemplo (ficticio) necesita su permiso", texto1);
        Assert.Contains("STOP", texto1);
        Assert.Contains("/privacidad.html", texto1);
        Assert.Contains("error", r1["pasos"]![2]!["resultado"]!.ToString());

        var r2 = (await (await app.Cliente.PostAsJsonAsync("/api/chat",
            new { sesionId = r1["sesionId"]!.ToString(), mensaje = "Sí, acepto", telefono = "8095550101" })).Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("Quedó registrado su permiso", r2["respuesta"]!.ToString());

        var registros = await ConDb(app, db => db.Consentimientos.ToListAsync());
        Assert.Equal(3, registros.Count);
        Assert.All(registros, c =>
        {
            Assert.Equal("otorgado", c.Estado);
            Assert.Equal("web", c.Canal);
            Assert.Equal(ServicioConsentimiento.VersionTexto, c.VersionTexto);
            Assert.Equal("8095550101", c.Telefono);
        });
    }

    [Theory]
    [InlineData("Quiero hablar con una persona", "es", "Con gusto. Ya avisé al personal")]
    [InlineData("I want to talk to a person", "en", "a person will contact you")]
    [InlineData("Mwen vle pale ak yon moun", "ht", "yon moun ap kontakte w")]
    public async Task Hablar_con_una_persona_escala_sin_pasar_por_el_modelo(string mensaje, string idioma, string esperado)
    {
        var (app, _) = Crear(new ScriptedChatModel());
        using var __ = app;

        var r = (await (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje, telefono = "8095550101" }))
            .Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Contains(esperado, r["respuesta"]!.ToString());
        Assert.Equal(idioma, r["idioma"]!.ToString());
        Assert.Empty(app.Modelo.Llamadas);
        var esc = await ConDb(app, db => db.Escalamientos.SingleAsync());
        Assert.Equal("El paciente pidió hablar con una persona.", esc.Motivo);
        Assert.Equal(1, esc.PacienteId);
        Assert.False(esc.Urgente);
    }

    [Fact]
    public async Task El_primer_mensaje_presenta_al_asistente_con_su_nombre_configurable()
    {
        var modelo = new ScriptedChatModel().ThenText("¿En qué le ayudo?").ThenText("Claro.").ThenText("Hola.");
        var (app, _) = Crear(modelo);
        using var __ = app;

        var r1 = (await (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola", telefono = "8095550101" }))
            .Content.ReadFromJsonAsync<JsonObject>())!;
        var r2 = (await (await app.Cliente.PostAsJsonAsync("/api/chat",
            new { sesionId = r1["sesionId"]!.ToString(), mensaje = "Una cita", telefono = "8095550101" })).Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.StartsWith("Hola, le atiende Clara, el asistente automático de Consultorio Ejemplo (ficticio).", r1["respuesta"]!.ToString());
        Assert.Equal("Claro.", r2["respuesta"]!.ToString());

        await app.IniciarSesionAsync();
        var put = await app.Cliente.PutAsJsonAsync("/api/panel/consultorio", new { nombreAsistente = "Luz" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var r3 = (await (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola", telefono = "8295550102" }))
            .Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.StartsWith("Hola, le atiende Luz,", r3["respuesta"]!.ToString());
    }

    [Fact]
    public async Task Una_respuesta_con_codigo_se_reemplaza_por_el_mensaje_de_alcance_sin_escalar()
    {
        var modelo = new ScriptedChatModel().ThenText("Claro:\n```python\nprint(sorted(lista))\n```");
        var (app, _) = Crear(modelo);
        using var __ = app;

        var r = (await (await app.Cliente.PostAsJsonAsync("/api/chat",
            new { mensaje = "Escríbeme un programa en Python", telefono = "8095550101" })).Content.ReadFromJsonAsync<JsonObject>())!;

        var texto = r["respuesta"]!.ToString();
        Assert.DoesNotContain("```", texto);
        Assert.EndsWith(Api.Idiomas.Textos.Obtener("agente.fuera_de_alcance", "es"), texto);
        Assert.Equal(0, await ConDb(app, db => db.Escalamientos.CountAsync()));
        Assert.Equal(1, await ConDb(app, db => db.Bitacora.CountAsync(b => b.Accion == "fuera_de_alcance")));
    }

    [Fact]
    public async Task La_secretaria_registra_el_permiso_presencial()
    {
        var (app, reloj) = Crear();
        using var __ = app;
        await SinConsentimientos(app);
        await app.IniciarSesionAsync();

        var texto = (await app.Cliente.GetFromJsonAsync<JsonObject>("/api/panel/consentimiento/texto?idioma=ht"))!;
        Assert.Contains("Consultorio Ejemplo (ficticio)", texto["texto"]!.ToString());
        Assert.Contains("STOP", texto["texto"]!.ToString());

        var paciente = (await app.Cliente.GetFromJsonAsync<JsonObject>("/api/panel/pacientes?telefono=809-555-0101"))!;
        Assert.Equal("PAC-001", paciente["id"]!.ToString());

        var res = await app.Cliente.PostAsJsonAsync("/api/panel/pacientes/PAC-001/consentimientos",
            new { acepta = true, categorias = new[] { "recordatorios" }, idioma = "es" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await RecordatorioDeMariaAsync(app, reloj);
        Assert.Equal([("recordatorio", "pendiente")], await AvisosAsync(app)); // en la cola de salida

        var registro = await ConDb(app, db => db.Consentimientos.SingleAsync());
        Assert.Equal(("presencial", "secretaria:secretaria"), (registro.Canal, registro.RegistradoPor));

        var invalida = await app.Cliente.PostAsJsonAsync("/api/panel/pacientes/PAC-001/consentimientos",
            new { acepta = true, categorias = new[] { "marketing" } });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.StatusCode);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    [InlineData("ht")]
    public void Las_plantillas_cumplen_el_formato_de_meta(string idioma)
    {
        // Meta rechaza cuerpos que empiezan o terminan con una variable, o con variables pegadas.
        var claves = Api.Idiomas.Textos.Claves(idioma).Where(c => c.StartsWith("agenda.", StringComparison.Ordinal)).ToList();
        Assert.Contains("agenda.seguimiento_contacto", claves);
        Assert.Contains("agenda.recordatorio_7dias", claves);
        foreach (var clave in claves)
        {
            var cuerpo = Api.Idiomas.Textos.Obtener(clave, idioma);
            Assert.False(cuerpo.TrimStart().StartsWith('{'), $"{clave} ({idioma}) empieza con una variable.");
            Assert.False(cuerpo.TrimEnd().EndsWith('}'), $"{clave} ({idioma}) termina con una variable.");
            Assert.DoesNotMatch(@"\}\s*\{", cuerpo);
            Assert.Contains("{0}", cuerpo);
            Assert.Contains("STOP", cuerpo);
        }
    }

    [Fact]
    public void Las_plantillas_son_de_utilidad_nombran_al_consultorio_y_explican_la_baja()
    {
        var hora = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));
        foreach (var idioma in new[] { "es", "en", "ht" })
        {
            var mensajes = new[]
            {
                TextosAgenda.Oferta(idioma, "Consultorio X (ficticio)", "María", "Dra. Ana", hora, hora),
                TextosAgenda.OfertaVencida(idioma, "Consultorio X (ficticio)", hora),
                TextosAgenda.Recordatorio(idioma, "Consultorio X (ficticio)", "María", "Dra. Ana", hora, "CITA-1"),
            };
            Assert.All(mensajes, m =>
            {
                Assert.Contains("Consultorio X (ficticio)", m.Texto);
                Assert.Contains("STOP", m.Texto);
                Assert.Equal(idioma, m.Idioma);
            });
        }
    }
}
