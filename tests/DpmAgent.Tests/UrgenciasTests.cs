using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

public class UrgenciasTests
{
    private const string TelMaria = "8095550101";
    private const string TelMedico = "8495550111";
    private static readonly TimeSpan Rd = TimeSpan.FromHours(-4);
    private static readonly DateTimeOffset LunesMediodia = new(2026, 10, 26, 12, 0, 0, Rd);

    private static (AppDePrueba App, RelojAjustable Reloj) Crear(ScriptedChatModel? modelo = null, DateTimeOffset? ahora = null)
    {
        var reloj = new RelojAjustable(ahora ?? LunesMediodia);
        var app = new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
        });
        return (app, reloj);
    }

    private static Task<AgentReply> WhatsApp(AppDePrueba app, string mensaje, string telefono = TelMaria) =>
        app.EnAmbito(sp => sp.GetRequiredService<ReceptionAgent>().RunAsync("wa-" + telefono, mensaje, telefono, CancellationToken.None));

    private static Task<T> Urgencias<T>(AppDePrueba app, Func<ServicioUrgencias, Task<T>> f) =>
        app.EnAmbito(sp => f(sp.GetRequiredService<ServicioUrgencias>()));

    /// <summary>Ocupa todos los turnos normales de esos días con José, para que no quede hueco.</summary>
    private static async Task LlenarAgendaAsync(AppDePrueba app, params DateOnly[] dias)
    {
        foreach (var dia in dias)
        foreach (var medico in new[] { "MED-01", "MED-02" })
        foreach (var t in await app.ConGateway(g => g.ConsultarDisponibilidadAsync(medico, dia, CancellationToken.None)))
            await app.ConGateway(g => g.CrearCitaAsync("PAC-002", medico, t, "control", CancellationToken.None));
    }

    [Fact]
    public async Task Pedido_urgente_con_hueco_ofrece_el_primero_y_siempre_recuerda_el_numero_de_emergencias()
    {
        var hoy14 = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 26), new TimeOnly(14, 0));
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("pedir_cita_urgente", new { })
            .ThenText("Hoy tengo a las 2:00 p. m. con la Dra. Valdez. ¿Le sirve?")
            .ThenTool("reservar_urgente", new { medico_id = "MED-01", fecha_hora = hoy14.ToString("yyyy-MM-dd'T'HH:mm:sszzz") })
            .ThenText("Listo, quedó para hoy a las 2:00 p. m.");
        var (app, _) = Crear(modelo);
        using var __ = app;

        var r1 = await WhatsApp(app, "Necesito que me vean hoy, es urgente");
        var resultado = JsonNode.Parse(r1.Pasos[1].Resultado)!;
        Assert.Null(resultado["solicitud_sobrecupo"]);
        Assert.Equal("2026-10-26", resultado["dias"]![0]!.ToString());
        Assert.Contains("14:00", resultado["opciones"]![0]!["fecha_hora"]!.ToString());
        // El recordatorio lo pone el servidor, no el modelo.
        Assert.EndsWith("Si es una emergencia, llame al 911 o vaya a la emergencia más cercana.", r1.Respuesta);

        await WhatsApp(app, "Sí");
        var cita = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Citas.SingleAsync(c => c.PacienteId == 1));
        Assert.Equal((1, hoy14.UtcDateTime), (cita.MedicoId, cita.FechaHoraUtc));
    }

    [Fact]
    public void La_herramienta_urgente_no_recibe_sintomas_ni_el_paciente_puede_aprobar_sobrecupos()
    {
        var tools = new AgentTools(new MockDpmGateway());
        var urgente = tools.Definitions.Single(d => d!["function"]!["name"]!.ToString() == "pedir_cita_urgente")!;
        Assert.Equal(["medico_id"], urgente["function"]!["parameters"]!["properties"]!.AsObject().Select(p => p.Key));
        Assert.DoesNotContain(tools.Definitions, d => d!["function"]!["name"]!.ToString().Contains("sobrecupo"));
    }

    [Fact]
    public async Task Los_cupos_de_urgencia_se_reservan_hasta_la_hora_de_liberacion()
    {
        var (app, reloj) = Crear(ahora: new DateTimeOffset(2026, 10, 26, 9, 0, 0, Rd));
        using var _ = app;
        await app.IniciarSesionAsync();
        var r = await app.Cliente.PostAsJsonAsync("/api/panel/urgencias/cupos", new { medicoId = "MED-01", diaSemana = 1, hora = "16:30" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var lunes = new DateOnly(2026, 10, 26);
        var cupo = HorarioConsultorio.EnHoraRd(lunes, new TimeOnly(16, 30));
        Task<IReadOnlyList<DateTimeOffset>> Libres(DateOnly d) => app.ConGateway(g => g.ConsultarDisponibilidadAsync("MED-01", d, CancellationToken.None));

        Assert.DoesNotContain(cupo, await Libres(lunes));
        Assert.DoesNotContain(HorarioConsultorio.EnHoraRd(new DateOnly(2026, 11, 2), new TimeOnly(16, 30)), await Libres(new DateOnly(2026, 11, 2))); // otro lunes
        Assert.Contains(HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(16, 30)), await Libres(new DateOnly(2026, 10, 27))); // martes: sin cupo
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.ConGateway(g => g.CrearCitaAsync("PAC-002", "MED-01", cupo, "control", CancellationToken.None)));

        reloj.Ahora = new DateTimeOffset(2026, 10, 26, 10, 1, 0, Rd); // a las 10 se libera para citas normales
        Assert.Contains(cupo, await Libres(lunes));
    }

    [Fact]
    public async Task Sin_turnos_normales_se_ofrece_el_cupo_de_urgencia()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        await app.Cliente.PutAsJsonAsync("/api/panel/urgencias/config", new { horaLiberarCupos = 17 });
        await app.Cliente.PostAsJsonAsync("/api/panel/urgencias/cupos", new { medicoId = "MED-01", diaSemana = 1, hora = "16:30" });
        await LlenarAgendaAsync(app, new DateOnly(2026, 10, 26));

        var r = await Urgencias(app, u => u.PedirAsync("PAC-001", null, "s1", CancellationToken.None));
        var opcion = Assert.Single(r.Opciones);
        Assert.True(opcion.CupoUrgencia);
        Assert.Null(r.Solicitud);

        var cita = await Urgencias(app, u => u.ReservarAsync("PAC-001", "MED-01", opcion.FechaHora, CancellationToken.None));
        Assert.Equal("PAC-001", cita.PacienteId);
        // Ya ocupado: no se le vuelve a ofrecer a otro paciente.
        Assert.DoesNotContain((await Urgencias(app, u => u.PedirAsync("PAC-002", null, "s2", CancellationToken.None))).Opciones, o => o.FechaHora == opcion.FechaHora);
    }

    [Fact]
    public async Task Sin_hueco_se_pide_sobrecupo_y_solo_el_medico_lo_aprueba()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("pedir_cita_urgente", new { medico_id = "MED-02" })
            .ThenText("No hay espacio hoy. Le pasé su pedido a la secretaria y al médico; le avisamos por aquí.")
            .ThenTool("decidir_sobrecupo", new { solicitud_id = "SOB-001", decision = "aprobar", hora = "17:30" })
            .ThenText("¿Confirma aprobar a María Rodríguez hoy a las 17:30?")
            .ThenTool("decidir_sobrecupo", new { solicitud_id = "SOB-001", decision = "aprobar", hora = "17:30" })
            .ThenText("Aprobado; ya le avisamos a la paciente.");
        var (app, _) = Crear(modelo);
        using var __ = app;
        await app.IniciarSesionAsync();
        var codigo = (await (await app.Cliente.PostAsJsonAsync("/api/panel/medicos/MED-02/telefono", new { telefono = TelMedico }))
            .Content.ReadFromJsonAsync<JsonObject>())!["codigo"]!.ToString();
        await WhatsApp(app, $"VERIFICAR {codigo}", TelMedico);
        await app.Cliente.PostAsJsonAsync("/api/panel/medicos/MED-02/resumen", new { acepta = true });
        await LlenarAgendaAsync(app, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27));

        var r1 = await WhatsApp(app, "Necesito que me vea el Dr. Herrera hoy, por favor");
        Assert.Equal("SOB-001", JsonNode.Parse(r1.Pasos[1].Resultado)!["solicitud_sobrecupo"]!.ToString());
        Assert.Contains("llame al 911", r1.Respuesta);

        // Aviso inmediato al médico por su canal, y la solicitud en el panel.
        var aviso = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().MensajesSalientes.SingleAsync(m => m.Destino == TelMedico && m.Tipo == "sobrecupo"));
        Assert.Contains("María Rodríguez (ficticia)", aviso.Texto);
        Assert.Contains("APROBAR SOB-001", aviso.Texto);
        var panel = (await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/urgencias/sobrecupos?estado=pendiente"))!;
        Assert.Equal("SOB-001", Assert.Single(panel)!["id"]!.ToString());

        // El médico aprueba en dos pasos.
        await WhatsApp(app, "APROBAR SOB-001 17:30", TelMedico);
        Assert.Equal("pendiente", (await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().SolicitudesSobrecupo.SingleAsync())).Estado);
        await WhatsApp(app, "Sí", TelMedico);

        var s = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().SolicitudesSobrecupo.SingleAsync());
        Assert.Equal(("aprobada", "medico:MED-02"), (s.Estado, s.DecididaPor));
        var hora = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 26), new TimeOnly(17, 30)).UtcDateTime;
        Assert.True(await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Citas.AnyAsync(c => c.PacienteId == 1 && c.MedicoId == 2 && c.FechaHoraUtc == hora)));
        var paciente = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().MensajesSalientes.SingleAsync(m => m.Destino == TelMaria && m.Tipo == "sobrecupo"));
        Assert.Contains("5:30", paciente.Texto);
        Assert.Contains("911", paciente.Texto);
    }

    [Fact]
    public async Task La_secretaria_rechaza_y_el_paciente_recibe_la_respuesta()
    {
        var (app, _) = Crear();
        using var __ = app;
        await LlenarAgendaAsync(app, new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27));
        var r = await Urgencias(app, u => u.PedirAsync("PAC-001", null, "wa-" + TelMaria, CancellationToken.None));
        Assert.Empty(r.Opciones);
        // Otro pedido el mismo día no duplica la solicitud.
        Assert.Equal(r.Solicitud, (await Urgencias(app, u => u.PedirAsync("PAC-001", null, "wa-" + TelMaria, CancellationToken.None))).Solicitud);

        await app.IniciarSesionAsync();
        var sinHora = await app.Cliente.PostAsJsonAsync($"/api/panel/urgencias/sobrecupos/{r.Solicitud}/decidir", new { aprobar = true });
        Assert.Equal(HttpStatusCode.BadRequest, sinHora.StatusCode);
        var res = await app.Cliente.PostAsJsonAsync($"/api/panel/urgencias/sobrecupos/{r.Solicitud}/decidir", new { aprobar = false });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PostAsJsonAsync($"/api/panel/urgencias/sobrecupos/{r.Solicitud}/decidir", new { aprobar = false })).StatusCode);

        var m = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().MensajesSalientes.SingleAsync(x => x.Destino == TelMaria && x.Tipo == "sobrecupo"));
        Assert.Contains("no fue posible", m.Texto);
        Assert.Contains("911", m.Texto);
        Assert.Equal("sobrecupo_rechazado", m.Plantilla);
    }

    [Fact]
    public async Task Fuera_de_horario_da_emergencias_y_centros_y_pasa_al_siguiente_dia_habil()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("pedir_cita_urgente", new { })
            .ThenText("Mañana lunes tengo a las 8:00 a. m.");
        var viernesNoche = new DateTimeOffset(2026, 10, 30, 19, 0, 0, Rd);
        var (app, _) = Crear(modelo, viernesNoche);
        using var __ = app;
        await app.IniciarSesionAsync();
        await app.Cliente.PutAsJsonAsync("/api/panel/urgencias/config", new { centrosEmergencia = "Hospital Ejemplo Norte (ficticio)\nClínica Ejemplo Sur (ficticia)" });
        await app.Cliente.PostAsJsonAsync("/api/panel/feriados", new { fecha = "2026-11-02", nombre = "Feriado de prueba" });

        var r = await WhatsApp(app, "I need to be seen as soon as possible");
        var resultado = JsonNode.Parse(r.Pasos[1].Resultado)!;
        Assert.True(resultado["fuera_de_horario"]!.GetValue<bool>());
        Assert.Equal("2026-11-03", Assert.Single(resultado["dias"]!.AsArray())!.ToString()); // lunes feriado → martes
        Assert.Contains("911", r.Respuesta);
        Assert.Contains("Hospital Ejemplo Norte (ficticio)", r.Respuesta);
        Assert.Contains("Clínica Ejemplo Sur (ficticia)", r.Respuesta);
    }

    [Fact]
    public async Task Los_cupos_y_la_configuracion_validan_y_la_configuracion_es_solo_del_admin()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PostAsJsonAsync("/api/panel/urgencias/cupos", new { medicoId = "MED-01", diaSemana = 6, hora = "09:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PostAsJsonAsync("/api/panel/urgencias/cupos", new { medicoId = "MED-09", diaSemana = 1, hora = "09:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PutAsJsonAsync("/api/panel/urgencias/config", new { horaLiberarCupos = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.PostAsJsonAsync("/api/panel/urgencias/cupos", new { medicoId = "MED-01", diaSemana = 1, hora = "09:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Cliente.PostAsJsonAsync("/api/panel/urgencias/cupos", new { medicoId = "MED-01", diaSemana = 1, hora = "09:00" })).StatusCode);
    }
}
