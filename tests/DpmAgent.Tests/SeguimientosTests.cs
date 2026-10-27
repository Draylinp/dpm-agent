using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

public class SeguimientosTests
{
    // Lunes 26 de octubre de 2026, 12:00 en República Dominicana.
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

    private static Task<SeguimientoInfo> CrearSeguimiento(AppDePrueba app, DateOnly fecha, int tolerancia = 3, string paciente = "PAC-001",
        int? recurrencia = null, string? unidad = null) =>
        app.EnAmbito(sp => sp.GetRequiredService<ServicioSeguimientos>()
            .CrearAsync(new NuevoSeguimiento(paciente, "MED-01", fecha, tolerancia, "control", recurrencia, unidad), "secretaria:prueba", CancellationToken.None));

    private static Task<ResultadoSeguimientos> Motor(AppDePrueba app) =>
        app.EnAmbito(sp => sp.GetRequiredService<ServicioSeguimientos>().ProcesarAsync(CancellationToken.None));

    private static Task<List<(string Tipo, string? Plantilla, string Estado)>> Avisos(AppDePrueba app) =>
        app.EnAmbito(async sp => (await sp.GetRequiredService<DpmDbContext>().MensajesSalientes.OrderBy(m => m.Id)
            .Select(m => new { m.Tipo, m.Plantilla, m.Estado }).ToListAsync()).Select(m => (m.Tipo, m.Plantilla, m.Estado)).ToList());

    private static Task<E.Seguimiento> Leer(AppDePrueba app, string id) =>
        app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Seguimientos.AsNoTracking().SingleAsync(s => s.Id == IdsDpm.Leer(id, "SEG")));

    [Fact]
    public async Task A_dos_semanas_se_agenda_de_una_vez_en_el_primer_hueco_de_la_ventana()
    {
        var (app, _) = Crear();
        using var _ = app;

        // Lunes 9 de noviembre ± 2 días: el sábado y el domingo no hay consulta, así que queda el lunes a las 8:00.
        var s = await CrearSeguimiento(app, new DateOnly(2026, 11, 9), tolerancia: 2);

        Assert.Equal(ServicioSeguimientos.Agendado, s.Estado);
        Assert.Equal(HorarioConsultorio.EnHoraRd(new DateOnly(2026, 11, 9), new TimeOnly(8, 0)), s.CitaFechaHora);
        Assert.Equal([("seguimiento", "seguimiento_agendado", "pendiente")], await Avisos(app));
    }

    [Theory]
    [InlineData(2027, 4, 26)] // 6 meses
    [InlineData(2027, 10, 26)] // 1 año
    public async Task Lejos_queda_pendiente_y_se_contacta_21_dias_antes(int anio, int mes, int dia)
    {
        var (app, reloj) = Crear();
        using var _ = app;
        var objetivo = new DateOnly(anio, mes, dia);

        var s = await CrearSeguimiento(app, objetivo);
        Assert.Equal(ServicioSeguimientos.Pendiente, s.Estado);
        Assert.Equal([("seguimiento", "seguimiento_creado", "pendiente")], await Avisos(app));

        reloj.Ahora = HorarioConsultorio.EnHoraRd(objetivo.AddDays(-22), new TimeOnly(9, 0));
        Assert.Equal(0, (await Motor(app)).Contactos);

        reloj.Ahora = HorarioConsultorio.EnHoraRd(objetivo.AddDays(-21), new TimeOnly(9, 0));
        Assert.Equal(1, (await Motor(app)).Contactos);
        var leido = await Leer(app, s.Id);
        Assert.Equal((ServicioSeguimientos.Contactando, 1), (leido.Estado, leido.Intentos));
        Assert.Equal("seguimiento_contacto", (await Avisos(app))[^1].Plantilla);
    }

    [Fact]
    public async Task Un_feriado_en_la_fecha_objetivo_se_respeta()
    {
        var (app, _) = Crear();
        using var _ = app;
        await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            db.Feriados.Add(new E.Feriado { ConsultorioId = 1, Fecha = new DateOnly(2026, 11, 9), Nombre = "Feriado de prueba (ficticio)" });
            return await db.SaveChangesAsync();
        });

        var sinMargen = await CrearSeguimiento(app, new DateOnly(2026, 11, 9), tolerancia: 0);
        var conMargen = await CrearSeguimiento(app, new DateOnly(2026, 11, 9), tolerancia: 1, paciente: "PAC-002");

        Assert.Equal(ServicioSeguimientos.Contactando, sinMargen.Estado); // no hay hueco: se le pregunta al paciente
        Assert.Equal(new DateOnly(2026, 11, 10), DateOnly.FromDateTime(conMargen.CitaFechaHora!.Value.ToOffset(TimeSpan.FromHours(-4)).DateTime));
    }

    [Fact]
    public async Task Sin_respuesta_tras_tres_intentos_pasa_a_la_lista_de_la_secretaria()
    {
        var (app, reloj) = Crear();
        using var _ = app;
        var s = await CrearSeguimiento(app, new DateOnly(2026, 11, 16), tolerancia: 0);
        await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Citas.Where(c => c.Id > 0).ExecuteUpdateAsync(x => x.SetProperty(c => c.Estado, "cancelada")));
        await Motor(app); // la cita se canceló: el seguimiento se reabre
        await Motor(app); // y se contacta (intento 1)

        for (var i = 0; i < 2; i++)
        {
            reloj.Avanzar(TimeSpan.FromDays(3));
            Assert.Equal(1, (await Motor(app)).Reintentos);
        }

        reloj.Avanzar(TimeSpan.FromDays(3));
        Assert.Equal(1, (await Motor(app)).SinRespuesta);
        Assert.Equal((ServicioSeguimientos.SinRespuesta, 3), ((await Leer(app, s.Id)).Estado, (await Leer(app, s.Id)).Intentos));

        await app.IniciarSesionAsync();
        var lista = (await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/seguimientos?estado=sin_respuesta"))!;
        Assert.Equal(s.Id, Assert.Single(lista)!["id"]!.ToString());
    }

    [Fact]
    public async Task Al_pasar_la_cita_de_un_seguimiento_recurrente_se_crea_el_siguiente()
    {
        var (app, reloj) = Crear();
        using var _ = app;
        var s = await CrearSeguimiento(app, new DateOnly(2026, 11, 9), tolerancia: 0, recurrencia: 3, unidad: "meses");

        reloj.Ahora = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(1, (await Motor(app)).Recurrencias);
        Assert.Equal(0, (await Motor(app)).Recurrencias);

        var todos = await app.EnAmbito(sp => sp.GetRequiredService<ServicioSeguimientos>().ListarAsync(null, CancellationToken.None));
        var siguiente = Assert.Single(todos, x => x.Id != s.Id);
        Assert.Equal(new DateOnly(2027, 2, 9), siguiente.FechaObjetivo);
        Assert.Equal(ServicioSeguimientos.Pendiente, siguiente.Estado);
        Assert.Equal("3 meses", siguiente.Recurrencia);
    }

    [Fact]
    public async Task Sin_permiso_no_se_envia_y_queda_para_llamar_sin_reintentos()
    {
        var (app, reloj) = Crear();
        using var _ = app;
        await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Consentimientos.ExecuteDeleteAsync());

        var s = await CrearSeguimiento(app, new DateOnly(2026, 11, 9), tolerancia: 0);
        await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Citas.ExecuteUpdateAsync(x => x.SetProperty(c => c.Estado, "cancelada")));
        await Motor(app);
        await Motor(app);
        reloj.Avanzar(TimeSpan.FromDays(10));
        var r = await Motor(app);

        Assert.True((await Leer(app, s.Id)).PorLlamada);
        Assert.Equal(0, r.Reintentos);
        Assert.All(await Avisos(app), a => Assert.Equal("llamar", a.Estado));
    }

    [Fact]
    public async Task El_paciente_agenda_su_seguimiento_solo_dentro_de_la_ventana()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("seguimientos_del_paciente", new { })
            .ThenTool("crear_cita", new { medico_id = "MED-01", fecha_hora = "2027-01-04T09:00:00-04:00", motivo = "control", seguimiento_id = "SEG-001" })
            .ThenTool("crear_cita", new { medico_id = "MED-01", fecha_hora = "2027-04-26T09:00:00-04:00", motivo = "control", seguimiento_id = "SEG-001" })
            .ThenText("Listo, su control quedó para el lunes 26 de abril a las 9:00 a. m.");
        var (app, reloj) = Crear(modelo);
        using var _ = app;
        await CrearSeguimiento(app, new DateOnly(2027, 4, 26), tolerancia: 5);
        reloj.Ahora = new DateTimeOffset(2027, 4, 5, 9, 0, 0, TimeSpan.FromHours(-4));
        await Motor(app);

        var r = (await (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Sí, búsqueme un horario", telefono = "8095550101" }))
            .Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Contains("2027-04-21", r["pasos"]![1]!["resultado"]!.ToString()); // ventana desde
        Assert.Contains("error", r["pasos"]![2]!["resultado"]!.ToString()); // enero: fuera de la ventana
        Assert.Contains("\"creada\":true", r["pasos"]![3]!["resultado"]!.ToString());
        Assert.Equal(ServicioSeguimientos.Agendado, (await Leer(app, "SEG-001")).Estado);
    }

    [Fact]
    public async Task Panel_crea_seguimiento_avisa_si_falta_permiso_y_un_bloqueo_marca_citas_para_reprogramar()
    {
        var (app, _) = Crear();
        using var _ = app;
        await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Consentimientos.Where(c => c.PacienteId == 2).ExecuteDeleteAsync());
        await app.IniciarSesionAsync();

        var res = await app.Cliente.PostAsJsonAsync("/api/panel/seguimientos",
            new { pacienteId = "PAC-002", medicoId = "MED-01", enCantidad = 2, enUnidad = "semanas", toleranciaDias = 2 });
        var json = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(json["requiereConsentimiento"]!.GetValue<bool>());
        Assert.Equal("agendado", json["seguimiento"]!["estado"]!.ToString());

        var bloqueo = await app.Cliente.PostAsJsonAsync("/api/panel/bloqueos",
            new { medicoId = "MED-01", desde = "2026-11-09", hasta = "2026-11-13", motivo = "congreso" });
        Assert.Equal(1, (await bloqueo.Content.ReadFromJsonAsync<JsonObject>())!["citasParaReprogramar"]!.GetValue<int>());
        Assert.Empty(await app.ConGateway(g => g.ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 11, 10), CancellationToken.None)));

        var invalido = await app.Cliente.PostAsJsonAsync("/api/panel/seguimientos",
            new { pacienteId = "PAC-002", medicoId = "MED-01", fechaObjetivo = "2026-10-01", toleranciaDias = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
    }
}
