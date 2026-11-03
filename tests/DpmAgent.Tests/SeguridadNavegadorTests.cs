using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace DpmAgent.Tests;

/// <summary>F29: CSP estricta, cabeceras de protección, defensa CSRF por origen y API sin CORS.</summary>
public class SeguridadNavegadorTests
{
    [Theory]
    [InlineData("/", "microphone=(self)")]
    [InlineData("/panel.html", "microphone=()")]
    [InlineData("/privacidad.html", "microphone=()")]
    [InlineData("/api/demo", "microphone=()")]
    public async Task Las_paginas_y_la_api_llevan_las_cabeceras_de_proteccion(string ruta, string microfono)
    {
        using var app = new AppDePrueba();
        var r = await app.Cliente.GetAsync(ruta);
        string H(string nombre) => string.Join(",", r.Headers.TryGetValues(nombre, out var v) ? v : r.Content.Headers.TryGetValues(nombre, out var c) ? c : []);

        var csp = H("Content-Security-Policy");
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        foreach (var directiva in new[] { "script-src 'self'", "style-src 'self'", "frame-ancestors 'none'", "object-src 'none'", "base-uri 'self'", "form-action 'self'" })
            Assert.Contains(directiva, csp);
        Assert.Equal("DENY", H("X-Frame-Options"));
        Assert.Equal("nosniff", H("X-Content-Type-Options"));
        Assert.Equal("same-origin", H("Cross-Origin-Opener-Policy"));
        Assert.Contains(microfono, H("Permissions-Policy"));
        Assert.Contains("camera=()", H("Permissions-Policy"));
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("panel.html")]
    [InlineData("privacidad.html")]
    public void Las_paginas_no_tienen_scripts_ni_estilos_en_linea(string pagina)
    {
        var html = File.ReadAllText(Path.Combine(Directory.GetParent(DpmAgent.Eval.Casos.CarpetaEval())!.FullName, "src", "DpmAgent.Api", "wwwroot", pagina));
        Assert.DoesNotMatch(new Regex(@"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<style", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotContain("javascript:", html);
    }

    [Fact]
    public async Task El_panel_rechaza_cambios_desde_otro_origen_o_sin_origen()
    {
        using var app = new AppDePrueba();
        await app.IniciarSesionAsync();
        var propio = app.Cliente.BaseAddress!.GetLeftPart(UriPartial.Authority);
        app.Cliente.DefaultRequestHeaders.Remove("Origin");

        async Task<HttpStatusCode> Enviar(string? origen, string? referer)
        {
            var m = new HttpRequestMessage(HttpMethod.Post, "/api/panel/urgencias/cupos") { Content = JsonContent.Create(new { medicoId = "MED-01", diaSemana = 1, hora = "09:00" }) };
            if (origen is not null) m.Headers.Add("Origin", origen);
            if (referer is not null) m.Headers.Referrer = new Uri(referer);
            return (await app.Cliente.SendAsync(m)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await Enviar("https://sitio-malicioso.example", null));
        Assert.Equal(HttpStatusCode.Forbidden, await Enviar(null, null));
        Assert.Equal(HttpStatusCode.Forbidden, await Enviar("null", "https://sitio-malicioso.example/x"));
        Assert.Equal(HttpStatusCode.Forbidden, await Enviar(propio.Replace("localhost", "localhost.example"), null));
        Assert.Equal(HttpStatusCode.OK, await Enviar(null, propio + "/panel.html"));
        Assert.Equal(HttpStatusCode.Conflict, await Enviar(propio, null)); // mismo cupo: pasó la defensa y llegó al endpoint

        // Leer no cambia nada: no se exige origen. El login también está protegido.
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.GetAsync("/api/panel/urgencias/cupos")).StatusCode);
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { usuario = "secretaria", clave = AppDePrueba.ClaveSecretaria }) };
        login.Headers.Add("Origin", "https://sitio-malicioso.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Cliente.SendAsync(login)).StatusCode);
    }

    [Fact]
    public async Task La_api_no_acepta_otros_origenes_por_cors()
    {
        using var app = new AppDePrueba();
        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/chat");
        preflight.Headers.Add("Origin", "https://sitio-malicioso.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var r = await app.Cliente.SendAsync(preflight);
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));

        var get = new HttpRequestMessage(HttpMethod.Get, "/api/demo");
        get.Headers.Add("Origin", "https://sitio-malicioso.example");
        var g = await app.Cliente.SendAsync(get);
        Assert.False(g.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(g.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
