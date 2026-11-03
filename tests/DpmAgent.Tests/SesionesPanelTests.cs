using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Data;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

/// <summary>F30: sesiones del panel, claves, login sin enumeración, segundo factor y bitácora de accesos.</summary>
public class SesionesPanelTests
{
    private static (AppDePrueba App, RelojAjustable Reloj) Crear()
    {
        var reloj = new RelojAjustable(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.FromHours(-4)));
        var app = new AppDePrueba(servicios: s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
        });
        return (app, reloj);
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string usuario, string clave, string? codigo = null) =>
        c.PostAsJsonAsync("/api/auth/login", new { usuario, clave, codigo });

    [Fact]
    public async Task La_sesion_vence_por_inactividad_y_a_las_8_horas_aunque_haya_actividad()
    {
        var (app, reloj) = Crear();
        using var _ = app;
        await app.IniciarSesionAsync();

        reloj.Avanzar(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode);

        await app.IniciarSesionAsync();
        for (var i = 0; i < 23; i++) // 23 × 20 min = 7 h 40 min de actividad: sigue
        {
            reloj.Avanzar(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode);
        }

        reloj.Avanzar(TimeSpan.FromMinutes(25)); // 8 h 5 min desde el inicio
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode);
    }

    [Fact]
    public async Task La_clave_inicial_se_cambia_en_el_primer_inicio_y_cumple_la_politica()
    {
        var (app, _) = Crear();
        using var __ = app;
        var r = await Login(app.Cliente, "secretaria", AppDePrueba.ClaveSecretaria);
        Assert.Equal("cambiar_clave", (await r.Content.ReadFromJsonAsync<JsonObject>())!["restriccion"]!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode);
        Assert.Equal("cambiar_clave", (await app.Cliente.GetFromJsonAsync<JsonObject>("/api/auth/yo"))!["restriccion"]!.ToString());

        foreach (var debil in new[] { "corta-123", "Consultorio2026!", "Password123456", "secretaria-2026-clave", "123456789012", "aaaaaaaaaaaaaaa" })
        {
            var cambio = await app.Cliente.PostAsJsonAsync("/api/auth/clave", new { actual = AppDePrueba.ClaveSecretaria, nueva = debil });
            Assert.Equal(HttpStatusCode.BadRequest, cambio.StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest,
            (await app.Cliente.PostAsJsonAsync("/api/auth/clave", new { actual = "no-es-la-clave", nueva = "luna-verde-sobre-el-rio" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await app.Cliente.PostAsJsonAsync("/api/auth/clave", new { actual = AppDePrueba.ClaveSecretaria, nueva = "luna-verde-sobre-el-rio" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode);
    }

    [Fact]
    public async Task Cambiar_la_clave_cierra_las_otras_sesiones()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        using var otro = app.NuevoCliente();
        Assert.Equal(HttpStatusCode.OK, (await Login(otro, "secretaria", AppDePrueba.ClaveSecretariaCambiada)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otro.GetAsync("/api/panel/citas")).StatusCode);

        var cambio = await app.Cliente.PostAsJsonAsync("/api/auth/clave", new { actual = AppDePrueba.ClaveSecretariaCambiada, nueva = "tercera-clave-del-panel" });
        Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await otro.GetAsync("/api/panel/citas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.GetAsync("/api/panel/citas")).StatusCode); // la propia sigue
    }

    [Fact]
    public async Task El_login_no_revela_que_usuarios_existen()
    {
        var (app, _) = Crear();
        using var __ = app;
        async Task<(HttpStatusCode, string, long)> Intento(string usuario)
        {
            var reloj = Stopwatch.StartNew();
            var r = await Login(app.Cliente, usuario, "una-clave-que-no-es");
            return (r.StatusCode, await r.Content.ReadAsStringAsync(), reloj.ElapsedMilliseconds);
        }

        await Intento("calentar");
        var (s1, c1, t1) = await Intento("no-existe-nadie");
        var (s2, c2, t2) = await Intento("secretaria");
        Assert.Equal((s1, c1), (s2, c2));
        Assert.Equal(HttpStatusCode.Unauthorized, s1);
        // Siempre se calcula un hash de clave: el usuario inexistente no responde mucho más rápido.
        Assert.True(t1 * 4 >= t2, $"inexistente {t1} ms, clave mal {t2} ms");
    }

    [Fact]
    public async Task El_segundo_factor_pide_un_codigo_valido_que_no_se_reutiliza()
    {
        var (app, reloj) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        var inicio = (await (await app.Cliente.PostAsync("/api/auth/totp/iniciar", null)).Content.ReadFromJsonAsync<JsonObject>())!;
        var secreto = Base32(inicio["clave"]!.ToString());
        Assert.StartsWith("otpauth://totp/", inicio["uri"]!.ToString());

        Assert.Equal(HttpStatusCode.BadRequest, (await app.Cliente.PostAsJsonAsync("/api/auth/totp/activar", new { codigo = "000000" })).StatusCode);
        var codigo = Totp.Codigo(secreto, Totp.Paso(reloj.GetUtcNow()));
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.PostAsJsonAsync("/api/auth/totp/activar", new { codigo })).StatusCode);

        using var otro = app.NuevoCliente();
        var sinCodigo = await Login(otro, "secretaria", AppDePrueba.ClaveSecretariaCambiada);
        Assert.True((await sinCodigo.Content.ReadFromJsonAsync<JsonObject>())!["segundoFactor"]!.GetValue<bool>());
        Assert.Equal(HttpStatusCode.Unauthorized, (await otro.GetAsync("/api/panel/citas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(otro, "secretaria", AppDePrueba.ClaveSecretariaCambiada, "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(otro, "secretaria", AppDePrueba.ClaveSecretariaCambiada, codigo)).StatusCode); // ya usado

        reloj.Avanzar(TimeSpan.FromSeconds(30));
        var nuevo = Totp.Codigo(secreto, Totp.Paso(reloj.GetUtcNow()));
        Assert.Equal(HttpStatusCode.OK, (await Login(otro, "secretaria", AppDePrueba.ClaveSecretariaCambiada, nuevo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otro.GetAsync("/api/panel/citas")).StatusCode);

        // El secreto se guarda protegido, nunca en claro.
        var guardado = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().UsuariosPanel.Select(u => u.TotpSecreto).SingleAsync(u => u != null));
        Assert.DoesNotContain(inicio["clave"]!.ToString(), guardado!);
    }

    [Fact]
    public async Task El_consultorio_puede_exigir_el_segundo_factor_a_los_administradores()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.PutAsJsonAsync("/api/panel/seguridad", new { totpObligatorioAdmin = true })).StatusCode);

        using var otro = app.NuevoCliente();
        var r = await Login(otro, "secretaria", AppDePrueba.ClaveSecretariaCambiada);
        Assert.Equal("activar_totp", (await r.Content.ReadFromJsonAsync<JsonObject>())!["restriccion"]!.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.GetAsync("/api/panel/citas")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await otro.PostAsync("/api/auth/totp/iniciar", null)).StatusCode);
    }

    [Fact]
    public async Task La_bitacora_de_accesos_registra_ver_exportar_y_borrar_y_solo_la_ve_el_admin()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        await app.Cliente.GetAsync("/api/panel/pacientes?telefono=8095550101");
        await app.Cliente.GetAsync("/api/panel/pacientes/PAC-001/exportar");
        await app.Cliente.PostAsJsonAsync("/api/panel/pacientes/PAC-002/borrar", new { confirmacion = "BORRAR" });

        var accesos = (await app.Cliente.GetFromJsonAsync<JsonArray>("/api/panel/accesos"))!;
        Assert.Equal(["borrar:2", "exportar:1", "ver_ficha:1"], accesos.Select(a => $"{a!["accion"]}:{a["pacienteId"]}"));
        Assert.All(accesos, a => Assert.Equal("secretaria", a!["usuario"]!.ToString()));
        Assert.DoesNotContain("Rodríguez", accesos.ToJsonString());

        // Una secretaria (no admin) no la ve.
        await app.EnAmbito(async sp =>
        {
            await sp.GetRequiredService<ServicioUsuarios>().CrearAsync(1, "recepcion2", "Recepción (ficticia)", "girasol-azul-del-patio", "secretaria", CancellationToken.None);
            return await sp.GetRequiredService<DpmDbContext>().UsuariosPanel.Where(u => u.Usuario == "recepcion2").ExecuteUpdateAsync(x => x.SetProperty(u => u.DebeCambiarClave, false));
        });
        using var otro = app.NuevoCliente();
        await Login(otro, "recepcion2", "girasol-azul-del-patio");
        Assert.Equal(HttpStatusCode.Forbidden, (await otro.GetAsync("/api/panel/accesos")).StatusCode);
    }

    private static byte[] Base32(string texto)
    {
        const string alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in texto)
        {
            buffer = (buffer << 5) | alfabeto.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return bytes.ToArray();
    }
}
