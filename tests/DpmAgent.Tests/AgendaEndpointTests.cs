using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Dpm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

public class AgendaEndpointTests
{
    [Fact]
    public async Task El_recordatorio_llega_al_chat_web_del_paciente()
    {
        var reloj = new RelojAjustable(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4)));
        var modelo = new ScriptedChatModel().ThenTool("buscar_paciente", new { }).ThenText("Hola, María");
        using var app = new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
        });

        var chat = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "hola", telefono = "8095550101" });
        var sesionId = (await chat.Content.ReadFromJsonAsync<JsonObject>())!["sesionId"]!.ToString();
        var cita = await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01",
            HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)), "control", CancellationToken.None));

        reloj.Avanzar(TimeSpan.FromHours(7));
        await app.Services.GetRequiredService<AgendaHostedService>().VueltaAsync(CancellationToken.None);

        var salientes = await app.Cliente.GetFromJsonAsync<JsonArray>($"/api/chat/{sesionId}/salientes");
        var mensaje = Assert.Single(salientes!)!;
        Assert.Equal("recordatorio", mensaje["tipo"]!.ToString());
        Assert.Contains(cita.Id, mensaje["texto"]!.ToString());

        var despues = await app.Cliente.GetFromJsonAsync<JsonArray>($"/api/chat/{sesionId}/salientes?despuesDe={mensaje["id"]}");
        Assert.Empty(despues!);

        var otraSesion = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/chat/otra-sesion/salientes");
        Assert.Empty(otraSesion!);

        await app.IniciarSesionAsync();
        var bitacora = await app.Cliente.GetFromJsonAsync<JsonArray>("/api/bitacora");
        Assert.Contains(bitacora!, b => b!["accion"]!.ToString() == "recordatorio_enviado");
    }
}
