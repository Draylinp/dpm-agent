using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

/// <summary>F20: familias con un mismo teléfono, reprogramación masiva, retraso del médico y ausencias.</summary>
public class FamiliasTests
{
    private const string TelMaria = "8095550101";
    private const string TelJose = "8295550102";
    private const string TelMedico = "8495550111";
    private static readonly TimeSpan Rd = TimeSpan.FromHours(-4);
    private static readonly DateTimeOffset Lunes = new(2026, 10, 26, 12, 0, 0, Rd);
    private static readonly DateOnly Martes = new(2026, 10, 27);

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

    private static Task<AgentReply> WhatsApp(AppDePrueba app, string mensaje, string telefono = TelMaria) =>
        app.EnAmbito(sp => sp.GetRequiredService<ReceptionAgent>().RunAsync("wa-" + telefono, mensaje, telefono, CancellationToken.None));

    private static Task<T> Db<T>(AppDePrueba app, Func<DpmDbContext, Task<T>> f) => app.EnAmbito(sp => f(sp.GetRequiredService<DpmDbContext>()));

    private static Task<T> Rep<T>(AppDePrueba app, Func<ServicioReprogramacion, Task<T>> f) =>
        app.EnAmbito(sp => f(sp.GetRequiredService<ServicioReprogramacion>()));

    [Fact]
    public async Task Un_telefono_gestiona_a_su_familia_y_vincular_requiere_confirmacion()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("registrar_paciente", new { nombre = "Sofía Rodríguez (ficticia)" })
            .ThenTool("registrar_paciente", new { nombre = "Sofía Rodríguez (ficticia)" }) // mismo turno: todavía no
            .ThenText("¿Confirma que registro a Sofía como paciente vinculada a este número?")
            .ThenTool("registrar_paciente", new { nombre = "Sofía Rodríguez (ficticia)" })
            .ThenText("Listo, Sofía quedó registrada.")
            // Conversación nueva desde el mismo número: hay que saber para quién es.
            .ThenTool("buscar_paciente", new { })
            .ThenTool("citas_del_paciente", new { })
            .ThenTool("seleccionar_paciente", new { paciente_id = "PAC-002" }) // José es de otro número
            .ThenTool("seleccionar_paciente", new { paciente_id = "PAC-003" })
            .ThenText("Perfecto, ¿qué día le sirve para Sofía?");
        var (app, _) = Crear(modelo);
        using var __ = app;

        var r1 = await WhatsApp(app, "Quiero registrar a mi hija Sofía");
        Assert.Contains("requiere_confirmacion", r1.Pasos[1].Resultado);
        Assert.Contains("requiere_confirmacion", r1.Pasos[2].Resultado);
        Assert.Equal(2, await Db(app, db => db.Pacientes.CountAsync()));

        var r2 = await WhatsApp(app, "Sí");
        Assert.Contains("\"vinculado\":true", r2.Pasos[0].Resultado);
        var familia = await app.ConGateway(g => g.PacientesDelTelefonoAsync(TelMaria, CancellationToken.None));
        Assert.Equal(["PAC-001", "PAC-003"], familia.Select(p => p.Id));

        var r3 = await app.EnAmbito(sp => sp.GetRequiredService<ReceptionAgent>().RunAsync("wa-nueva", "Una cita para mi hija", TelMaria, CancellationToken.None));
        var busqueda = JsonNode.Parse(r3.Pasos[0].Resultado)!;
        Assert.True(busqueda["varios"]!.GetValue<bool>());
        Assert.Null(busqueda["seleccionado"]);
        Assert.Contains("identificar al paciente", r3.Pasos[1].Resultado);
        Assert.Contains("No hay un paciente PAC-002 vinculado a este número", r3.Pasos[2].Resultado);
        Assert.Contains("\"seleccionado\":true", r3.Pasos[3].Resultado);
        Assert.DoesNotContain("José", string.Join(" ", r3.Pasos.Select(p => p.Resultado)));
    }

    [Fact]
    public async Task Stop_da_de_baja_a_toda_la_familia_y_los_avisos_van_a_nombre_del_paciente()
    {
        var (app, _) = Crear(new ScriptedChatModel());
        using var __ = app;
        var sofia = await app.ConGateway(g => g.RegistrarPacienteAsync("Sofía Rodríguez (ficticia)", TelMaria, null, CancellationToken.None));
        await app.EnAmbito(sp => sp.GetRequiredService<ServicioConsentimiento>().RegistrarAsync(3, null, true, CanalesConsentimiento.Presencial, "es", "prueba", null, CancellationToken.None).ContinueWith(_ => 0));

        // Un aviso para Sofía deja la conversación del número a nombre de Sofía.
        await app.EnAmbito(sp => sp.GetRequiredService<Notificador>().NotificarPacienteAsync(3, (i, c) => new MensajeAgenda($"Aviso de {c} para Sofía. STOP", "x", [], i), "recordatorio", CancellationToken.None));
        var sesion = await app.EnAmbito(sp => sp.GetRequiredService<IConversationStore>().ObtenerAsync("wa-" + TelMaria, CancellationToken.None));
        Assert.Equal(sofia.Id, sesion.PacienteId);

        await WhatsApp(app, "STOP");
        foreach (var id in new[] { 1, 3 })
            Assert.False(await app.EnAmbito(sp => sp.GetRequiredService<ServicioConsentimiento>().VigenteAsync(id, CategoriasConsentimiento.Recordatorios, CancellationToken.None)));
        Assert.True(await app.EnAmbito(sp => sp.GetRequiredService<ServicioConsentimiento>().VigenteAsync(2, CategoriasConsentimiento.Recordatorios, CancellationToken.None)));
    }

    [Fact]
    public async Task El_bloqueo_reprograma_con_prioridad_para_seguimientos_y_el_paciente_escoge()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        // José: cita normal a las 9:00; María: cita de un seguimiento a las 10:00. José no dio permiso de avisos.
        var jose = await app.ConGateway(g => g.CrearCitaAsync("PAC-002", "MED-01", HorarioConsultorio.EnHoraRd(Martes, new TimeOnly(9, 0)), "control", CancellationToken.None));
        var maria = await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(Martes, new TimeOnly(10, 0)), "control", CancellationToken.None));
        await Db(app, async db =>
        {
            db.Seguimientos.Add(new Api.Data.Entidades.Seguimiento
            {
                ConsultorioId = 1, PacienteId = 1, MedicoId = 1, FechaObjetivo = Martes, ToleranciaDias = 7, Motivo = "control", Estado = "agendado",
                CitaId = IdsDpm.Leer(maria.Id, "CITA"), CreadoPor = "prueba", CreadoUtc = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var c = await db.Citas.FirstAsync(x => x.Id == IdsDpm.Leer(maria.Id, "CITA"));
            c.SeguimientoId = (await db.Seguimientos.SingleAsync()).Id;
            await db.Consentimientos.Where(x => x.PacienteId == 2).ExecuteDeleteAsync();
            return await db.SaveChangesAsync();
        });

        var b = await app.Cliente.PostAsJsonAsync("/api/panel/bloqueos", new { medicoId = "MED-01", desde = "2026-10-27", hasta = "2026-10-27", motivo = "congreso" });
        Assert.Equal(HttpStatusCode.OK, b.StatusCode);

        var r = await Rep(app, s => s.ProcesarAsync(CancellationToken.None));
        Assert.Equal((1, 1), (r.Contactados, r.ParaLlamar));
        Assert.Equal(0, (await Rep(app, s => s.ProcesarAsync(CancellationToken.None))).Contactados); // una sola vez

        // María (seguimiento) recibe los primeros horarios: hoy a las 2:00, 2:30 y 3:00.
        var aviso = await Db(app, db => db.MensajesSalientes.SingleAsync(m => m.Tipo == "reprogramar" && m.Estado != Notificador.EstadoLlamar));
        Assert.Equal(TelMaria, aviso.Destino);
        Assert.Contains("2:00", aviso.Texto);
        Assert.Contains("(cita " + maria.Id + ")", aviso.Texto);

        // Ella escoge las 2:30 hablando con el agente.
        var nueva = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 26), new TimeOnly(14, 30));
        var modelo = app.Modelo
            .ThenTool("buscar_paciente", new { })
            .ThenTool("citas_del_paciente", new { })
            .ThenTool("reprogramar_cita", new { cita_id = jose.Id, fecha_hora = nueva.ToString("yyyy-MM-dd'T'HH:mm:sszzz") }) // la de otro: no
            .ThenTool("reprogramar_cita", new { cita_id = maria.Id, fecha_hora = nueva.ToString("yyyy-MM-dd'T'HH:mm:sszzz") })
            .ThenText("Listo, quedó para hoy a las 2:30.");
        var turno = await WhatsApp(app, "La de las 2:30 por favor");
        Assert.Contains("\"para_reprogramar\":true", turno.Pasos[1].Resultado);
        Assert.Contains($"No existe la cita {jose.Id}", turno.Pasos[2].Resultado);
        Assert.Contains("\"reprogramada\":true", turno.Pasos[3].Resultado);

        var lista = await Rep(app, s => s.ListarAsync(CancellationToken.None));
        var deMaria = lista.Single(x => x.Id == maria.Id);
        Assert.Equal(("reprogramada", true), (deMaria.Estado, deMaria.Prioridad));
        Assert.NotNull(deMaria.NuevaCitaId);
        Assert.Equal("llamar", lista.Single(x => x.Id == jose.Id).Estado);
        var seg = await Db(app, db => db.Seguimientos.SingleAsync());
        Assert.Equal(IdsDpm.Leer(deMaria.NuevaCitaId, "CITA"), seg.CitaId);
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.GetAsync("/api/panel/reprogramaciones")).StatusCode);
    }

    [Fact]
    public async Task El_retraso_avisa_solo_a_los_pacientes_que_quedan_hoy_y_el_medico_lo_confirma()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("avisar_retraso", new { minutos = 40 })
            .ThenText("Avisaré a 1 paciente. ¿Confirma?")
            .ThenTool("avisar_retraso", new { minutos = 40 })
            .ThenText("Listo.");
        var (app, _) = Crear(modelo);
        using var __ = app;
        await app.IniciarSesionAsync();
        var codigo = (await (await app.Cliente.PostAsJsonAsync("/api/panel/medicos/MED-01/telefono", new { telefono = TelMedico }))
            .Content.ReadFromJsonAsync<JsonObject>())!["codigo"]!.ToString();
        await WhatsApp(app, $"VERIFICAR {codigo}", TelMedico);

        var hoy = new DateOnly(2026, 10, 26);
        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(hoy, new TimeOnly(15, 0)), "control", CancellationToken.None));
        await app.ConGateway(g => g.CrearCitaAsync("PAC-002", "MED-01", HorarioConsultorio.EnHoraRd(Martes, new TimeOnly(9, 0)), "control", CancellationToken.None));

        await WhatsApp(app, "Voy con 40 minutos de retraso, avísales", TelMedico);
        Assert.Equal(0, await Db(app, db => db.MensajesSalientes.CountAsync(m => m.Tipo == "retraso")));
        await WhatsApp(app, "Sí", TelMedico);

        var m = await Db(app, db => db.MensajesSalientes.SingleAsync(x => x.Tipo == "retraso"));
        Assert.Equal(TelMaria, m.Destino);
        Assert.Contains("40 minutos", m.Texto);
        Assert.Contains("3:00", m.Texto);

        var panel = await app.Cliente.PostAsJsonAsync("/api/panel/medicos/MED-01/retraso", new { minutos = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, panel.StatusCode);
    }

    [Fact]
    public async Task No_asistio_ofrece_reagendar_una_vez_y_cuenta_las_ausencias()
    {
        var (app, reloj) = Crear();
        using var __ = app;
        var pasada = await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(Martes, new TimeOnly(9, 0)), "control", CancellationToken.None));
        var futura = await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(Martes, new TimeOnly(16, 0)), "control", CancellationToken.None));
        reloj.Ahora = new DateTimeOffset(2026, 10, 27, 11, 0, 0, Rd);
        await app.IniciarSesionAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PostAsync($"/api/panel/citas/{futura.Id}/no-asistio", null)).StatusCode);
        var r = await app.Cliente.PostAsync($"/api/panel/citas/{pasada.Id}/no-asistio", null);
        Assert.Equal(1, (await r.Content.ReadFromJsonAsync<JsonObject>())!["ausencias"]!.GetValue<int>());
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PostAsync($"/api/panel/citas/{pasada.Id}/no-asistio", null)).StatusCode);

        var aviso = await Db(app, db => db.MensajesSalientes.SingleAsync(m => m.Tipo == "ausencia"));
        Assert.Contains("no pudo asistir", aviso.Texto);
        var citas = (await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/citas?fecha=2026-10-27"))!;
        Assert.All(citas, c => Assert.Equal(1, c!["ausencias"]!.GetValue<int>()));
    }

    [Fact]
    public async Task El_panel_vincula_familiares_solo_con_confirmacion()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        var sin = await app.Cliente.PostAsJsonAsync("/api/panel/familias", new { telefono = TelMaria, nombre = "Pedro Rodríguez (ficticio)", confirmado = false });
        Assert.Equal(HttpStatusCode.BadRequest, sin.StatusCode);
        var con = await app.Cliente.PostAsJsonAsync("/api/panel/familias", new { telefono = TelMaria, nombre = "Pedro Rodríguez (ficticio)", confirmado = true });
        Assert.Equal(HttpStatusCode.OK, con.StatusCode);
        // Un número nuevo no necesita confirmación.
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.PostAsJsonAsync("/api/panel/familias", new { telefono = "8495550177", nombre = "Rosa Peña (ficticia)", confirmado = false })).StatusCode);

        var familia = (await app.Cliente.GetFromJsonAsync<JsonArray>($"/api/panel/familias?telefono={TelMaria}"))!;
        Assert.Equal(["María Rodríguez (ficticia)", "Pedro Rodríguez (ficticio)"], familia.Select(m => m!["nombre"]!.ToString()));
    }
}
