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

    /// <summary>Inicia sesión como la secretaria inicial; la cookie queda en <see cref="Cliente"/>.</summary>
    public async Task IniciarSesionAsync()
    {
        var res = await Cliente.PostAsJsonAsync("/api/auth/login", new { usuario = "secretaria", clave = ClaveSecretaria });
        res.EnsureSuccessStatusCode();
    }

    public Task<T> ConGateway<T>(Func<IDpmGateway, Task<T>> accion) =>
        EnAmbito(sp => accion(sp.GetRequiredService<IDpmGateway>()));

    public void Dispose()
    {
        Cliente.Dispose();
        _factory.Dispose();
    }
}
