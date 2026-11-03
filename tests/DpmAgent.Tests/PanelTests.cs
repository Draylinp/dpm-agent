using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

public class PanelTests
{
    private static readonly DateTimeOffset Lunes = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private static (AppDePrueba App, RelojAjustable Reloj) Crear()
    {
        var reloj = new RelojAjustable(Lunes);
        var app = new AppDePrueba(null, s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
        });
        return (app, reloj);
    }

    private static Task<HttpResponseMessage> Login(AppDePrueba app, string clave, string usuario = "secretaria") =>
        app.Cliente.PostAsJsonAsync("/api/auth/login", new { usuario, clave });

    [Theory]
    [InlineData("GET", "/api/panel/citas")]
    [InlineData("GET", "/api/panel/escalamientos")]
    [InlineData("GET", "/api/escalamientos")]
    [InlineData("GET", "/api/carnets")]
    [InlineData("GET", "/api/bitacora")]
    [InlineData("GET", "/api/lista-espera")]
    [InlineData("POST", "/api/carnets/CAR-1/confirmar")]
    [InlineData("POST", "/api/panel/escalamientos/ESC-1/atender")]
    public async Task Sin_sesion_los_endpoints_de_la_secretaria_dan_401(string metodo, string ruta)
    {
        var (app, _) = Crear();
        using var _app = app;

        var res = await app.Cliente.SendAsync(new HttpRequestMessage(new HttpMethod(metodo), ruta));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Inicia_y_cierra_sesion_con_cookie_segura()
    {
        var (app, _) = Crear();
        using var _app = app;

        var mal = await Login(app, "otra-clave-cualquiera");
        var noExiste = await Login(app, AppDePrueba.ClaveSecretaria, "nadie");
        var bien = await Login(app, AppDePrueba.ClaveSecretaria);

        Assert.Equal(HttpStatusCode.Unauthorized, mal.StatusCode);
        Assert.Equal(await mal.Content.ReadAsStringAsync(), await noExiste.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, bien.StatusCode);
        var cookie = bien.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("dpm.panel="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var yo = await app.Cliente.GetFromJsonAsync<JsonObject>("/api/auth/yo");
        Assert.Equal("admin", yo!["rol"]!.ToString());

        await app.Cliente.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Cliente.GetAsync("/api/auth/yo")).StatusCode);
    }

    [Fact]
    public async Task Cinco_intentos_fallidos_bloquean_la_cuenta_quince_minutos()
    {
        var (app, reloj) = Crear();
        using var _app = app;

        for (var i = 0; i < 5; i++)
            await Login(app, "clave-equivocada");

        var bloqueado = await Login(app, AppDePrueba.ClaveSecretaria);
        reloj.Avanzar(TimeSpan.FromMinutes(16));
        var despues = await Login(app, AppDePrueba.ClaveSecretaria);

        Assert.Equal(HttpStatusCode.Unauthorized, bloqueado.StatusCode);
        Assert.Equal(HttpStatusCode.OK, despues.StatusCode);
    }

    [Fact]
    public async Task Citas_del_dia_con_datos_del_paciente()
    {
        var (app, _) = Crear();
        using var _app = app;
        var martes = new DateOnly(2026, 10, 27);
        await app.ConGateway(g => g.CrearCitaAsync("PAC-002", "MED-02", HorarioConsultorio.EnHoraRd(martes, new TimeOnly(10, 0)), "control", CancellationToken.None));
        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(martes, new TimeOnly(8, 30)), "primera consulta", CancellationToken.None));
        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01", HorarioConsultorio.EnHoraRd(martes.AddDays(1), new TimeOnly(8, 0)), "control", CancellationToken.None));
        await app.IniciarSesionAsync();

        var citas = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/citas?fecha=2026-10-27");
        var malaFecha = await app.Cliente.GetAsync("/api/panel/citas?fecha=27-10-2026");

        Assert.Equal(2, citas!.Count);
        Assert.Equal("María Rodríguez (ficticia)", citas[0]!["paciente"]!.ToString());
        Assert.Equal("ARS Ejemplo", citas[0]!["ars"]!.ToString());
        Assert.Equal("Dr. Luis Herrera (ficticio)", citas[1]!["medico"]!.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, malaFecha.StatusCode);
    }

    [Fact]
    public async Task Atender_un_escalamiento_lo_saca_de_pendientes_y_queda_en_la_bitacora()
    {
        var (app, _) = Crear();
        using var _app = app;
        var normal = await app.ConGateway(g => g.RegistrarEscalamientoAsync("s1", "PAC-001", "Pregunta por resultados", false, CancellationToken.None));
        await app.ConGateway(g => g.RegistrarEscalamientoAsync("s2", null, "Posible emergencia", true, CancellationToken.None));
        await app.IniciarSesionAsync();

        var antes = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/escalamientos");
        var atender = await app.Cliente.PostAsync($"/api/panel/escalamientos/{normal.Id}/atender", null);
        var noExiste = await app.Cliente.PostAsync("/api/panel/escalamientos/ESC-999/atender", null);
        var despues = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/escalamientos");
        var bitacora = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/bitacora");

        Assert.Equal(2, antes!.Count);
        Assert.True(antes[0]!["urgente"]!.GetValue<bool>()); // los urgentes primero
        Assert.Equal(HttpStatusCode.OK, atender.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        Assert.Single(despues!);
        Assert.Contains(bitacora!, b => b!["accion"]!.ToString() == "escalamiento_atendido" && b["actor"]!.ToString() == "secretaria:secretaria");
    }

    [Fact]
    public async Task Una_secretaria_solo_ve_los_datos_de_su_consultorio()
    {
        var (app, _) = Crear();
        using var _app = app;

        // Segundo consultorio, con su propia secretaria y un escalamiento.
        string escOtro = await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            db.Consultorios.Add(new E.Consultorio { Nombre = "Otro consultorio (ficticio)", CreadoUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await sp.GetRequiredService<ServicioUsuarios>().CrearAsync(2, "secretaria2", "Otra (ficticia)", "otra-clave-larga", "secretaria", CancellationToken.None);
            // Como si ya hubiera cambiado su clave inicial.
            await db.UsuariosPanel.Where(u => u.Usuario == "secretaria2").ExecuteUpdateAsync(x => x.SetProperty(u => u.DebeCambiarClave, false));
            return "";
        });
        escOtro = await app.EnAmbito(async sp =>
        {
            sp.GetRequiredService<ConsultorioDelAmbito>().Establecer(2);
            return (await sp.GetRequiredService<IDpmGateway>().RegistrarEscalamientoAsync("s", null, "algo", false, CancellationToken.None)).Id;
        });

        await app.IniciarSesionAsync();
        var propios = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/escalamientos");
        var atenderAjeno = await app.Cliente.PostAsync($"/api/panel/escalamientos/{escOtro}/atender", null);

        Assert.Empty(propios!);
        Assert.Equal(HttpStatusCode.NotFound, atenderAjeno.StatusCode);

        await app.Cliente.PostAsync("/api/auth/logout", null);
        await Login(app, "otra-clave-larga", "secretaria2");
        var delOtro = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/escalamientos");
        Assert.Single(delOtro!);
        Assert.Empty((await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/citas?fecha=2026-10-27"))!);
    }

    [Fact]
    public async Task Solo_un_admin_crea_usuarios_y_con_clave_fuerte()
    {
        var (app, _) = Crear();
        using var _app = app;
        await app.IniciarSesionAsync();

        var corta = await app.Cliente.PostAsJsonAsync("/api/panel/usuarios", new { usuario = "ana", nombre = "Ana (ficticia)", clave = "corta", rol = "secretaria" });
        var creada = await app.Cliente.PostAsJsonAsync("/api/panel/usuarios", new { usuario = "ana", nombre = "Ana (ficticia)", clave = "una-clave-larga", rol = "secretaria" });
        var repetida = await app.Cliente.PostAsJsonAsync("/api/panel/usuarios", new { usuario = "ana", nombre = "Ana (ficticia)", clave = "una-clave-larga", rol = "secretaria" });

        await app.Cliente.PostAsync("/api/auth/logout", null);
        await Login(app, "una-clave-larga", "ana");
        var sinPermiso = await app.Cliente.PostAsJsonAsync("/api/panel/usuarios", new { usuario = "otro", nombre = "Otro", clave = "una-clave-larga", rol = "admin" });

        Assert.Equal(HttpStatusCode.BadRequest, corta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, creada.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, repetida.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sinPermiso.StatusCode);
    }

    [Fact]
    public async Task En_produccion_sin_clave_inicial_no_se_crea_ningun_usuario()
    {
        using var sqlite = new SqliteDePrueba(new FixedClock(Lunes));
        var db = sqlite.Contexto();
        var servicio = new ServicioUsuarios(db, Options.Create(new PanelOptions()), new FixedClock(Lunes), new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());

        await servicio.AsegurarUsuarioInicialAsync(esDesarrollo: false, NullLogger.Instance, CancellationToken.None);
        Assert.Empty(await db.UsuariosPanel.ToListAsync());

        await servicio.AsegurarUsuarioInicialAsync(esDesarrollo: true, NullLogger.Instance, CancellationToken.None);
        var u = Assert.Single(await db.UsuariosPanel.ToListAsync());
        Assert.Equal("admin", u.Rol);
        Assert.StartsWith("AQAAAA", u.HashClave); // formato de PasswordHasher, nunca la clave
    }
}
