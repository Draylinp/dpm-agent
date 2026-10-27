using System.Net.Http.Json;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

/// <summary>
/// La app completa en memoria (SQLite en memoria, modelo falso, sin tareas en segundo plano)
/// para probar los endpoints por HTTP.
/// </summary>
public sealed class AppDePrueba : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public AppDePrueba(
        ScriptedChatModel? modelo = null,
        Action<IServiceCollection>? servicios = null,
        IDictionary<string, string?>? ajustes = null)
    {
        Modelo = modelo ?? new ScriptedChatModel();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("BaseDatos:Proveedor", "Memoria");
            b.UseSetting("Agenda:MotorActivo", "false");
            b.UseSetting("WhatsApp:ProcesadorActivo", "false");
            b.UseSetting("Panel:ClaveInicial", ClaveSecretaria);
            // El modelo falso responde según un guion: el filtro usa solo las reglas para no consumirlo.
            b.UseSetting("FiltroSalida:Proveedor", "Reglas");
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string?>())
                b.UseSetting(clave, valor);

            b.ConfigureServices(s =>
            {
                s.RemoveAll<IChatModel>();
                s.AddSingleton<IChatModel>(Modelo);
                servicios?.Invoke(s);
            });
        });
        Cliente = _factory.CreateClient();
        // Como el navegador: las peticiones del panel llevan el origen del propio sitio (defensa CSRF).
        Cliente.DefaultRequestHeaders.Add("Origin", Cliente.BaseAddress!.GetLeftPart(UriPartial.Authority));
    }

    public const string ClaveSecretaria = "clave-de-prueba-123";

    public ScriptedChatModel Modelo { get; }

    public HttpClient Cliente { get; }

    public IServiceProvider Services => _factory.Services;

    /// <summary>Ejecuta algo con los servicios de un ámbito nuevo (como una petición aparte).</summary>
    public async Task<T> EnAmbito<T>(Func<IServiceProvider, Task<T>> accion)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await accion(scope.ServiceProvider);
    }

    /// <summary>Clave con la que queda la secretaria inicial después del cambio obligatorio del primer inicio de sesión.</summary>
    public const string ClaveSecretariaCambiada = "otra-clave-de-prueba-456";

    private string _claveActual = ClaveSecretaria;

    /// <summary>
    /// Inicia sesión como la secretaria inicial; la cookie queda en <see cref="Cliente"/>. La primera vez cambia la clave
    /// inicial, como exige el panel.
    /// </summary>
    public async Task IniciarSesionAsync()
    {
        var res = await Cliente.PostAsJsonAsync("/api/auth/login", new { usuario = "secretaria", clave = _claveActual });
        res.EnsureSuccessStatusCode();
        var cuerpo = await res.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
        if (cuerpo?["restriccion"]?.ToString() == "cambiar_clave")
        {
            (await Cliente.PostAsJsonAsync("/api/auth/clave", new { actual = _claveActual, nueva = ClaveSecretariaCambiada })).EnsureSuccessStatusCode();
            _claveActual = ClaveSecretariaCambiada;
        }
    }

    /// <summary>Otro navegador (sus propias cookies), con el origen del sitio.</summary>
    public HttpClient NuevoCliente()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("Origin", c.BaseAddress!.GetLeftPart(UriPartial.Authority));
        return c;
    }

    public Task<T> ConGateway<T>(Func<IDpmGateway, Task<T>> accion) =>
        EnAmbito(sp => accion(sp.GetRequiredService<IDpmGateway>()));

    public void Dispose()
    {
        Cliente.Dispose();
        _factory.Dispose();
    }
}
