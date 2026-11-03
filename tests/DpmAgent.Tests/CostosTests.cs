using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Costos;
using DpmAgent.Api.Data;
using DpmAgent.Api.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

public class CostosTests
{
    private static readonly Dictionary<string, string?> Precios = new()
    {
        ["Precios:Moneda"] = "USD",
        // Precios de prueba, no los de ningún proveedor.
        ["Precios:Modelos:modelo-a:EntradaPorMillon"] = "1",
        ["Precios:Modelos:modelo-a:SalidaPorMillon"] = "2",
    };

    private static UsoModelo Uso(string modelo, int entrada, int salida, string? sesion, int? consultorio = 1) =>
        new("Prueba", modelo, "agente", entrada, salida, 100, DateTimeOffset.UtcNow, consultorio, sesion);

    private static async Task<JsonObject> EsperarCostos(AppDePrueba app, int llamadas)
    {
        for (var i = 0; i < 50; i++)
        {
            var c = (await app.Cliente.GetFromJsonAsync<JsonObject>("/api/panel/costos"))!;
            if (c["llamadas"]!.GetValue<int>() >= llamadas)
                return c;
            await Task.Delay(100);
        }

        throw new TimeoutException("El uso no se guardó a tiempo.");
    }

    [Fact]
    public async Task El_panel_muestra_costo_por_conversacion_y_por_cita()
    {
        using var app = new AppDePrueba(ajustes: Precios);
        var registro = app.Services.GetRequiredService<IRegistroUso>();
        registro.Registrar(Uso("modelo-a", 1_000_000, 0, "s1"));
        registro.Registrar(Uso("modelo-a", 0, 500_000, "s1"));
        registro.Registrar(Uso("modelo-a", 1_000_000, 500_000, "s2"));
        await app.EnAmbito(async sp =>
        {
            var bitacora = sp.GetRequiredService<Bitacora>();
            await bitacora.RegistrarAsync(Bitacora.Agente, "cita_creada", "cita", "CITA-001", "s1", null, CancellationToken.None);
            await bitacora.RegistrarAsync(Bitacora.Sistema, "cita_creada", "cita", "CITA-002", null, null, CancellationToken.None);
            return 0;
        });

        await app.IniciarSesionAsync();
        var c = await EsperarCostos(app, 3);

        Assert.Equal(2, c["conversaciones"]!.GetValue<int>());
        Assert.Equal(1, c["citasAgendadas"]!.GetValue<int>()); // la del sistema no cuenta
        Assert.Equal(2_000_000, c["tokensEntrada"]!.GetValue<long>());
        Assert.Equal(1_000_000, c["tokensSalida"]!.GetValue<long>());
        Assert.Equal(4m, c["costoTotal"]!.GetValue<decimal>());     // 2 × 1 + 1 × 2
        Assert.Equal(2m, c["costoPorConversacion"]!.GetValue<decimal>());
        Assert.Equal(4m, c["costoPorCita"]!.GetValue<decimal>());
        Assert.Equal("USD", c["moneda"]!.GetValue<string>());
        Assert.Empty(c["modelosSinPrecio"]!.AsArray());
    }

    [Fact]
    public async Task Un_modelo_sin_precio_no_cuenta_como_gratis()
    {
        using var app = new AppDePrueba(ajustes: Precios);
        app.Services.GetRequiredService<IRegistroUso>().Registrar(Uso("modelo-sin-precio", 1000, 1000, "s1"));

        await app.IniciarSesionAsync();
        var c = await EsperarCostos(app, 1);

        Assert.Null(c["costoTotal"]);
        Assert.Null(c["costoPorConversacion"]);
        Assert.Equal(["modelo-sin-precio"], c["modelosSinPrecio"]!.AsArray().Select(m => m!.GetValue<string>()));
    }

    [Fact]
    public async Task El_uso_queda_en_su_consultorio_y_sin_contenido()
    {
        using var app = new AppDePrueba(ajustes: Precios);
        await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            db.Consultorios.Add(new E.Consultorio { Nombre = "Otro consultorio (ficticio)", CreadoUtc = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });

        var cola = app.Services.GetRequiredService<ColaUso>();
        cola.Registrar(Uso("modelo-a", 10, 10, "otro", consultorio: 2));
        cola.Registrar(Uso("modelo-a", 10, 10, "propio"));

        await app.IniciarSesionAsync();
        var c = await EsperarCostos(app, 1);
        await Task.Delay(200);
        c = (await app.Cliente.GetFromJsonAsync<JsonObject>("/api/panel/costos"))!;

        Assert.Equal(1, c["llamadas"]!.GetValue<int>());
        var propiedades = typeof(E.RegistroUsoModelo).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain(propiedades, p => p.Contains("Texto") || p.Contains("Contenido") || p.Contains("Mensaje"));
    }

    [Fact]
    public async Task Los_costos_son_solo_para_el_personal()
    {
        using var app = new AppDePrueba();
        var res = await app.Cliente.GetAsync("/api/panel/costos");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task El_agente_marca_las_llamadas_con_su_conversacion()
    {
        (string Proposito, int? Consultorio, string? Sesion)? visto = null;
        var modelo = new ScriptedChatModel().Then(_ =>
        {
            visto = (ContextoUso.Proposito, ContextoUso.ConsultorioId, ContextoUso.SesionId);
            return new JsonObject { ["role"] = "assistant", ["content"] = "Hola." };
        });
        using var app = new AppDePrueba(modelo);

        var res = await app.Cliente.PostAsJsonAsync("/api/chat", new { sesionId = "sesion-costos", mensaje = "Hola" });
        res.EnsureSuccessStatusCode();

        Assert.Equal(("agente", 1, "sesion-costos"), visto);
        Assert.Null(ContextoUso.SesionId);
    }

    [Fact]
    public void La_calculadora_lee_los_precios_de_la_configuracion()
    {
        var calc = new CalculadoraCosto(Options.Create(new PreciosOptions
        {
            Modelos = { ["m"] = new PrecioModelo { EntradaPorMillon = 0.5m, SalidaPorMillon = 1.5m } },
        }));

        Assert.Equal(0.002m, calc.Costo("m", 1000, 1000));
        Assert.Null(calc.Costo("otro", 1000, 1000));
    }
}
