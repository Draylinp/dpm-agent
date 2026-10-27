using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Demo;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

public class DemoTests
{
    // Lunes: el escenario pone la cita de María el martes 27 a las 9:00.
    private static readonly DateTimeOffset Lunes = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private static AppDePrueba Crear(bool habilitada = true, ScriptedChatModel? modelo = null, Dictionary<string, string?>? ajustes = null)
    {
        var todos = new Dictionary<string, string?> { ["Demo:Habilitado"] = habilitada ? "true" : "false" };
        foreach (var (k, v) in ajustes ?? [])
            todos[k] = v;

        return new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(new RelojAjustable(Lunes));
        }, todos);
    }

    private static Task<T> ConDb<T>(AppDePrueba app, Func<DpmDbContext, Task<T>> accion, int consultorio = 1) =>
        app.EnAmbito(sp =>
        {
            sp.GetRequiredService<ConsultorioDelAmbito>().Establecer(consultorio);
            return accion(sp.GetRequiredService<DpmDbContext>());
        });

    [Fact]
    public async Task Sin_demo_no_hay_ejemplos_ni_reinicio()
    {
        using var app = Crear(habilitada: false);

        var info = await app.Cliente.GetFromJsonAsync<JsonObject>("/api/demo");
        var reinicio = await app.Cliente.PostAsync("/api/demo/reiniciar", null);

        Assert.False(info!["habilitado"]!.GetValue<bool>());
        Assert.Empty(info["ejemplos"]!.AsArray());
        Assert.Equal(HttpStatusCode.Forbidden, reinicio.StatusCode);
        Assert.Equal(0, await ConDb(app, db => db.Citas.CountAsync()));
    }

    [Fact]
    public async Task Con_demo_hay_ocho_ejemplos_en_ambos_idiomas_y_el_escenario_listo()
    {
        using var app = Crear();

        var info = await app.Cliente.GetFromJsonAsync<JsonObject>("/api/demo");
        var ejemplos = info!["ejemplos"]!.AsArray();

        Assert.True(info["habilitado"]!.GetValue<bool>());
        Assert.Equal(8, ejemplos.Count);
        Assert.All(ejemplos, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e!["etiqueta"]!["es"]?.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(e["etiqueta"]!["en"]?.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(e["mensaje"]!["en"]?.GetValue<string>()));
        });

        var (citas, lista, sesiones) = await ConDb(app, async db => (
            await db.Citas.Select(c => new { c.PacienteId, c.FechaHoraUtc }).ToListAsync(),
            await db.ListaEspera.CountAsync(l => l.Estado == "activa"),
            await db.Conversaciones.Select(c => c.SesionId).ToListAsync()));

        var cita = Assert.Single(citas);
        Assert.Equal(HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)).UtcDateTime, cita.FechaHoraUtc);
        Assert.Equal(1, lista);
        Assert.Contains("demo-8295550102", sesiones);
        Assert.Contains("demo-8095550101", sesiones);

        // Fase 3: un control en 6 meses (María), uno que entra hoy en la ventana de contacto (José),
        // el médico de la demo con su número verificado y los cupos de urgencia.
        var (seguimientos, medico, cupos) = await ConDb(app, async db => (
            await db.Seguimientos.OrderBy(s => s.PacienteId).Select(s => s.FechaObjetivo).ToListAsync(),
            await db.Medicos.SingleAsync(m => m.Telefono == DatosDemo.TelefonoMedico),
            await db.CuposUrgencia.CountAsync()));
        Assert.Equal([new DateOnly(2027, 4, 26), new DateOnly(2026, 11, 16)], seguimientos);
        Assert.Equal("Dr. Luis Herrera (ficticio)", medico.Nombre);
        Assert.NotNull(medico.TelefonoVerificadoUtc);
        Assert.Equal(10, cupos);
    }

    [Fact]
    public async Task En_la_demo_el_ejemplo_del_medico_entra_por_el_canal_del_medico_y_el_de_hoy_recuerda_el_911()
    {
        var modelo = new ScriptedChatModel()
            .ThenText("¿Para qué paciente?")
            .ThenTool("pedir_cita_urgente", new { })
            .ThenText("Hoy tengo a las 2:00 p. m.");
        using var app = Crear(modelo: modelo);

        var r = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Control para María en 3 meses", telefono = DatosDemo.TelefonoMedico });
        r.EnsureSuccessStatusCode();
        Assert.Contains("Eres el asistente administrativo", modelo.Llamadas[0][0]["content"]!.ToString());

        var hoy = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Necesito que me vean hoy, por favor.", telefono = DatosDemo.TelefonoMaria });
        var json = (await hoy.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Contains("asistente de recepción", modelo.Llamadas[1][0]["content"]!.ToString());
        Assert.Contains("911", json["respuesta"]!.ToString());
    }

    [Fact]
    public async Task El_reinicio_restaura_la_semilla_y_no_toca_otro_consultorio()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("escalar_a_personal", new { resumen_es = "Pregunta por un trámite", resumen_en = "Asks about a procedure", urgente = false })
            .ThenText("Le paso con el personal.");
        using var app = Crear(modelo: modelo);

        // Uso de la demo: una conversación nueva con escalamiento.
        var chat = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola, una consulta sobre un trámite", telefono = "8095550199" });
        chat.EnsureSuccessStatusCode();

        // Otro consultorio (real, no de demo) con su propio paciente.
        await ConDb(app, async db =>
        {
            db.Consultorios.Add(new E.Consultorio { Nombre = "Otro consultorio (ficticio)", CreadoUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        });
        await ConDb(app, async db =>
        {
            db.Pacientes.Add(new E.Paciente { ConsultorioId = 2, Nombre = "Pedro Peña (ficticio)", Telefono = "8495550103", CreadoUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return 0;
        }, consultorio: 2);

        Assert.Equal(1, await ConDb(app, db => db.Escalamientos.CountAsync()));

        var res = await app.Cliente.PostAsync("/api/demo/reiniciar", null);
        res.EnsureSuccessStatusCode();

        var (escalamientos, pacientes, citas, conversaciones, medicos) = await ConDb(app, async db => (
            await db.Escalamientos.CountAsync(),
            await db.Pacientes.Select(p => p.Telefono).OrderBy(t => t).ToListAsync(),
            await db.Citas.CountAsync(),
            await db.Conversaciones.Select(c => c.SesionId).OrderBy(s => s).ToListAsync(),
            await db.Medicos.CountAsync()));

        Assert.Equal(0, escalamientos);
        Assert.Equal([DatosDemo.TelefonoMaria, DatosDemo.TelefonoJose], pacientes);
        Assert.Equal(1, citas);
        Assert.Equal(["demo-8095550101", "demo-8295550102"], conversaciones);
        Assert.Equal(2, medicos);

        var otro = await ConDb(app, db => db.Pacientes.Select(p => p.Nombre).ToListAsync(), consultorio: 2);
        Assert.Equal(["Pedro Peña (ficticio)"], otro);

        // La secretaria inicial sigue pudiendo entrar: los usuarios del panel no se borran.
        await app.IniciarSesionAsync();
    }

    [Fact]
    public async Task Si_Maria_cancela_la_oferta_llega_a_la_conversacion_demo_de_Jose()
    {
        using var app = Crear();

        await app.EnAmbito(async sp =>
        {
            var dpm = sp.GetRequiredService<IDpmGateway>();
            var cita = Assert.Single(await dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None));
            return await dpm.CancelarCitaAsync(cita.Id, "PAC-001", CancellationToken.None);
        });
        var resultado = await app.EnAmbito(sp => sp.GetRequiredService<MotorAgenda>().ProcesarAsync(CancellationToken.None));

        Assert.Equal(1, resultado.OfertasEnviadas);
        var oferta = await ConDb(app, db => db.MensajesSalientes.SingleAsync(m => m.Tipo == "oferta"));
        Assert.Equal(ServicioDemo.SesionDemo(DatosDemo.TelefonoJose), oferta.SesionId);
        Assert.Equal("web", oferta.Canal);
    }

    [Fact]
    public async Task Se_niega_a_reiniciar_un_consultorio_que_no_es_de_demo()
    {
        using var app = Crear();
        await ConDb(app, async db =>
        {
            var c = await db.Consultorios.SingleAsync();
            c.EsDemo = false;
            return await db.SaveChangesAsync();
        });

        var res = await app.Cliente.PostAsync("/api/demo/reiniciar", null);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal(1, await ConDb(app, db => db.Citas.CountAsync()));
    }

    [Fact]
    public async Task La_demo_limita_los_mensajes_diarios_por_ip()
    {
        var modelo = new ScriptedChatModel().ThenText("Hola.").ThenText("Claro.").ThenText("No debería llegar.");
        using var app = Crear(modelo: modelo, ajustes: new() { ["Demo:MensajesPorDiaPorIp"] = "2" });

        var r1 = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola" });
        var r2 = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola otra vez" });
        var r3 = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Y otra" });
        var info = await app.Cliente.GetAsync("/api/demo");

        Assert.True(r1.IsSuccessStatusCode);
        Assert.True(r2.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        Assert.Equal(2, modelo.Llamadas.Count);
        Assert.True(info.IsSuccessStatusCode);
    }

    [Fact]
    public async Task El_reinicio_tiene_limite_por_hora()
    {
        using var app = Crear(ajustes: new() { ["Demo:ReiniciosPorHoraPorIp"] = "1" });

        var r1 = await app.Cliente.PostAsync("/api/demo/reiniciar", null);
        var r2 = await app.Cliente.PostAsync("/api/demo/reiniciar", null);

        Assert.True(r1.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r2.StatusCode);
    }

    [Fact]
    public async Task Con_clave_de_panel_se_crea_el_usuario_demo()
    {
        using var app = Crear(ajustes: new() { ["Demo:ClavePanel"] = "demo-clave-larga-123" });

        var info = await app.Cliente.GetFromJsonAsync<JsonObject>("/api/demo");
        var login = await app.Cliente.PostAsJsonAsync("/api/auth/login", new { usuario = ServicioDemo.UsuarioPanel, clave = "demo-clave-larga-123" });

        Assert.Equal("demo", info!["usuarioPanel"]!.GetValue<string>());
        Assert.True(login.IsSuccessStatusCode);

        // Su clave está publicada en la página: no se le pide cambiarla y nadie puede cambiarla.
        Assert.Null((await login.Content.ReadFromJsonAsync<JsonObject>())!["restriccion"]);
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode);
        var cambio = await app.Cliente.PostAsJsonAsync("/api/auth/clave", new { actual = "demo-clave-larga-123", nueva = "Una-clave-distinta-2026" });
        Assert.Equal(HttpStatusCode.BadRequest, cambio.StatusCode);
    }
}
