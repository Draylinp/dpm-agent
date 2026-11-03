using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Data;
using DpmAgent.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

public class ProduccionTests
{
    [Fact]
    public async Task Salud_de_vida_y_de_listo()
    {
        using var app = new AppDePrueba(ajustes: new Dictionary<string, string?> { ["Meta:ApiKey"] = "" });

        var vida = await app.Cliente.GetFromJsonAsync<JsonObject>("/health/live");
        var listo = await app.Cliente.GetAsync("/health/ready");
        var json = (await listo.Content.ReadFromJsonAsync<JsonObject>())!;

        Assert.Equal("Healthy", vida!["estado"]!.ToString());
        Assert.Equal(HttpStatusCode.OK, listo.StatusCode); // degradado no tumba la instancia
        Assert.Equal("Healthy", json["componentes"]!["base_datos"]!["estado"]!.ToString());
        Assert.Equal(Environment.GetEnvironmentVariable("META_API_KEY") is { Length: > 0 } ? "Healthy" : "Degraded",
            json["componentes"]!["modelo"]!["estado"]!.ToString());
    }

    [Fact]
    public async Task Cabeceras_de_seguridad_y_correlacion()
    {
        using var app = new AppDePrueba();

        var propia = new HttpRequestMessage(HttpMethod.Get, "/api/medicos");
        propia.Headers.Add("X-Correlation-Id", "abc-123");
        var conId = await app.Cliente.SendAsync(propia);
        var sinId = await app.Cliente.GetAsync("/");
        var malo = new HttpRequestMessage(HttpMethod.Get, "/api/medicos");
        malo.Headers.Add("X-Correlation-Id", "<script>");
        var conIdMalo = await app.Cliente.SendAsync(malo);

        Assert.Equal("abc-123", conId.Headers.GetValues("X-Correlation-Id").Single());
        Assert.False(string.IsNullOrEmpty(sinId.Headers.GetValues("X-Correlation-Id").Single()));
        Assert.NotEqual("<script>", conIdMalo.Headers.GetValues("X-Correlation-Id").Single());
        Assert.Equal("nosniff", sinId.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", sinId.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", sinId.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Limite_por_ip_en_endpoints_publicos()
    {
        using var app = new AppDePrueba(ajustes: new Dictionary<string, string?> { ["Limites:PeticionesPorMinutoPorIp"] = "3" });

        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codigos.Add((await app.Cliente.GetAsync("/api/medicos")).StatusCode);
        var webhook = await app.Cliente.GetAsync("/api/whatsapp/webhook?hub.mode=subscribe");

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.TooManyRequests], codigos);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, webhook.StatusCode);
    }

    [Fact]
    public async Task Limite_de_intentos_de_login_por_ip()
    {
        using var app = new AppDePrueba(ajustes: new Dictionary<string, string?> { ["Limites:LoginPorMinutoPorIp"] = "2" });

        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            codigos.Add((await app.Cliente.PostAsJsonAsync("/api/auth/login", new { usuario = "x", clave = "y" })).StatusCode);

        Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests], codigos);
    }

    [Fact]
    public async Task La_retencion_borra_lo_viejo_y_conserva_lo_reciente()
    {
        var reloj = new RelojAjustable(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero));
        using var db = new SqliteDePrueba(reloj);

        // Conversación de hace 40 días y otra de ayer.
        var store = new DbConversationStore(db.Contexto(), reloj);
        foreach (var (id, dias) in new[] { ("vieja", 40), ("nueva", 1) })
        {
            reloj.Ahora = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero).AddDays(-dias);
            var s = await store.ObtenerAsync(id, CancellationToken.None);
            s.Mensajes.Add(new JsonObject { ["role"] = "user", ["content"] = "hola" });
            await store.GuardarAsync(s, CancellationToken.None);
        }

        var ctx = db.Contexto();
        var hace40 = new DateTime(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);
        ctx.MensajesEntrantes.AddRange(
            new E.MensajeEntrante { ConsultorioId = 1, ProveedorId = "w1", Telefono = "8095550101", Tipo = "text", Estado = "procesado", RecibidoUtc = hace40 },
            new E.MensajeEntrante { ConsultorioId = 1, ProveedorId = "w2", Telefono = "8095550101", Tipo = "text", Estado = "pendiente", RecibidoUtc = hace40 });
        ctx.Bitacora.Add(new E.RegistroBitacora { ConsultorioId = 1, Actor = "sistema", Accion = "vieja", FechaUtc = hace40.AddDays(-400) });
        ctx.Bitacora.Add(new E.RegistroBitacora { ConsultorioId = 1, Actor = "sistema", Accion = "reciente", FechaUtc = hace40 });
        await ctx.SaveChangesAsync();

        reloj.Ahora = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var r = await new ServicioRetencion(db.Contexto(), Options.Create(new RetencionOptions()), reloj).LimpiarAsync(CancellationToken.None);

        var despues = db.Contexto();
        Assert.Equal(1, r.Conversaciones);
        Assert.Equal(["nueva"], await despues.Conversaciones.Select(c => c.SesionId).ToListAsync());
        Assert.Equal(1, await despues.Mensajes.CountAsync());
        Assert.Equal(["w2"], await despues.MensajesEntrantes.Select(m => m.ProveedorId).ToListAsync()); // los pendientes no se tocan
        Assert.Equal(["reciente"], await despues.Bitacora.Select(b => b.Accion).ToListAsync());
    }
}
