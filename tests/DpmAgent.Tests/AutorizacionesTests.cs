using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DpmAgent.Tests;

public class AutorizacionesTests
{
    private static async Task<(AppDePrueba App, string CitaId)> CrearConCita()
    {
        var app = new AppDePrueba();
        var cita = await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01",
            HorarioConsultorio.EnHoraRd(DiaHabilEnUnaSemana(), new TimeOnly(9, 0)), "primera consulta", CancellationToken.None));
        await app.IniciarSesionAsync();
        return (app, cita.Id);
    }

    /// <summary>La app usa el reloj real: la cita va una semana adelante, en día de semana.</summary>
    private static DateOnly DiaHabilEnUnaSemana()
    {
        var dia = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);
        while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            dia = dia.AddDays(1);
        return dia;
    }

    private static async Task<JsonObject> Borrador(AppDePrueba app, string citaId)
    {
        var res = await app.Cliente.PostAsJsonAsync("/api/panel/autorizaciones", new { citaId });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    [Fact]
    public async Task El_borrador_trae_los_datos_administrativos_y_marca_lo_que_falta()
    {
        var (app, citaId) = await CrearConCita();
        using var _app = app;

        var a = await Borrador(app, citaId);
        var contenido = a["contenido"]!.ToString();

        Assert.Equal("borrador", a["estado"]!.ToString());
        Assert.Contains("BORRADOR, NO ENVIADO", contenido);
        Assert.Contains("ARS: ARS Ejemplo", contenido);
        Assert.Contains("Afiliado: María Rodríguez (ficticia)", contenido);
        Assert.Contains("Médico: Dra. Ana Valdez (ficticia)", contenido);
        Assert.Contains("Especialidad: Medicina General", contenido);
        Assert.Contains($"Referencia interna: {citaId}", contenido);
        Assert.Contains("[FALTA: número de afiliado]", contenido);
        Assert.Equal(["número de afiliado"], a["faltantes"]!.AsArray().Select(f => f!.ToString()));
    }

    [Fact]
    public async Task Pedirlo_dos_veces_devuelve_el_mismo_borrador()
    {
        var (app, citaId) = await CrearConCita();
        using var _app = app;

        var a1 = await Borrador(app, citaId);
        var a2 = await Borrador(app, citaId);

        Assert.Equal(a1["id"]!.ToString(), a2["id"]!.ToString());
        Assert.Single((await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/autorizaciones"))!);
    }

    [Fact]
    public async Task No_se_aprueba_con_datos_faltantes_y_aprobar_no_envia_nada()
    {
        var (app, citaId) = await CrearConCita();
        using var _app = app;
        var a = await Borrador(app, citaId);
        var id = a["id"]!.ToString();

        var conFalta = await app.Cliente.PostAsJsonAsync($"/api/panel/autorizaciones/{id}/aprobar", new { });
        var completo = a["contenido"]!.ToString().Replace("[FALTA: número de afiliado]", "AF-000123");
        var aprobada = await app.Cliente.PostAsJsonAsync($"/api/panel/autorizaciones/{id}/aprobar", new { contenido = completo });
        var editarDespues = await app.Cliente.PutAsJsonAsync($"/api/panel/autorizaciones/{id}", new { contenido = "otro" });

        Assert.Equal(HttpStatusCode.Conflict, conFalta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, aprobada.StatusCode);
        var json = (await aprobada.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("aprobada", json["estado"]!.ToString());
        Assert.Equal("secretaria", json["aprobadaPor"]!.ToString());
        Assert.Contains("AF-000123", json["contenido"]!.ToString());
        Assert.Equal(HttpStatusCode.Conflict, editarDespues.StatusCode);
        Assert.Empty((await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/autorizaciones"))!);

        // Nunca se envía solo: no hay ningún mensaje saliente.
        Assert.Empty(await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().MensajesSalientes.ToListAsync()));
        var bitacora = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/bitacora");
        Assert.Contains(bitacora!, b => b!["accion"]!.ToString() == "autorizacion_aprobada");
    }

    [Fact]
    public async Task Se_puede_editar_descartar_y_volver_a_crear()
    {
        var (app, citaId) = await CrearConCita();
        using var _app = app;
        var a = await Borrador(app, citaId);
        var id = a["id"]!.ToString();

        var editada = await app.Cliente.PutAsJsonAsync($"/api/panel/autorizaciones/{id}", new { contenido = "Texto revisado" });
        var muyLarga = await app.Cliente.PutAsJsonAsync($"/api/panel/autorizaciones/{id}", new { contenido = new string('x', 4001) });
        var descartada = await app.Cliente.PostAsync($"/api/panel/autorizaciones/{id}/descartar", null);
        var nueva = await Borrador(app, citaId);

        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, muyLarga.StatusCode);
        Assert.Equal(HttpStatusCode.OK, descartada.StatusCode);
        Assert.NotEqual(id, nueva["id"]!.ToString());
    }

    [Fact]
    public async Task Solo_para_citas_confirmadas_que_existen()
    {
        var (app, citaId) = await CrearConCita();
        using var _app = app;
        await app.ConGateway(g => g.CancelarCitaAsync(citaId, "PAC-001", CancellationToken.None));

        var cancelada = await app.Cliente.PostAsJsonAsync("/api/panel/autorizaciones", new { citaId });
        var noExiste = await app.Cliente.PostAsJsonAsync("/api/panel/autorizaciones", new { citaId = "CITA-999" });
        var aprobarNoExiste = await app.Cliente.PostAsJsonAsync("/api/panel/autorizaciones/AUT-999/aprobar", new { });

        Assert.Equal(HttpStatusCode.Conflict, cancelada.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, aprobarNoExiste.StatusCode);
    }
}
