using System.Text.Json.Nodes;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using Microsoft.Extensions.Logging.Abstractions;

namespace DpmAgent.Tests;

public class ReceptionAgentTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private static (ReceptionAgent Agent, AgentTools Tools, MockDpmGateway Dpm) Crear(ScriptedChatModel modelo)
    {
        var clock = new FixedClock(Ahora);
        var dpm = new MockDpmGateway(clock);
        var tools = new AgentTools(dpm);
        var agent = new ReceptionAgent(modelo, tools, new InMemoryConversationStore(), NullLogger<ReceptionAgent>.Instance, clock);
        return (agent, tools, dpm);
    }

    [Fact]
    public async Task Agenda_una_cita_en_varios_pasos()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("consultar_disponibilidad", new { medico_id = "MED-01", fecha = "2026-10-27" })
            .Then(msgs =>
            {
                var resultado = JsonNode.Parse(msgs[^1]["content"]!.ToString())!;
                var slot = resultado["horarios"]![0]!["fecha_hora"]!.ToString();
                return ScriptedChatModel.ToolCall("crear_cita",
                    new { medico_id = "MED-01", fecha_hora = slot, motivo = "control" });
            })
            .ThenText("Su cita quedó para el martes 27 a las 8:00 a. m.");

        var (agent, _, dpm) = Crear(modelo);

        var reply = await agent.RunAsync(null, "Quiero cita mañana temprano", "809-555-0101", CancellationToken.None);

        Assert.Equal(["buscar_paciente", "consultar_disponibilidad", "crear_cita"], reply.Pasos.Select(p => p.Herramienta));
        Assert.Contains("\"creada\":true", reply.Pasos[2].Resultado);
        Assert.Single(await dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None));
        Assert.EndsWith("Su cita quedó para el martes 27 a las 8:00 a. m.", reply.Respuesta);
    }

    [Fact]
    public async Task Envia_reglas_de_seguridad_en_el_prompt_de_sistema()
    {
        var modelo = new ScriptedChatModel().ThenText("Hola");
        var (agent, _, _) = Crear(modelo);

        await agent.RunAsync(null, "hola", CancellationToken.None);

        var sistema = modelo.Llamadas[0][0];
        Assert.Equal("system", sistema["role"]!.ToString());
        Assert.Contains("Nunca des orientación médica", sistema["content"]!.ToString());
        Assert.Contains("2026-10-26", sistema["content"]!.ToString());
    }

    [Fact]
    public async Task Pregunta_clinica_se_escala_al_personal()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("escalar_a_personal", new { motivo = "Pregunta sobre medicamento", urgente = false })
            .ThenText("Eso se lo responde la doctora. Ya le pasé su consulta al personal.");

        var (agent, _, dpm) = Crear(modelo);

        await agent.RunAsync("s1", "¿Puedo tomar ibuprofeno?", CancellationToken.None);

        var esc = Assert.Single(await dpm.ListarEscalamientosAsync(CancellationToken.None));
        Assert.Equal("s1", esc.SesionId);
        Assert.False(esc.Urgente);
    }

    [Fact]
    public async Task Conserva_el_historial_entre_turnos()
    {
        var modelo = new ScriptedChatModel().ThenText("¿Para qué día?").ThenText("Perfecto");
        var (agent, _, _) = Crear(modelo);

        var r1 = await agent.RunAsync(null, "Quiero una cita", CancellationToken.None);
        await agent.RunAsync(r1.SesionId, "Para el martes", CancellationToken.None);

        // system + user + assistant + user
        Assert.Equal(4, modelo.Llamadas[1].Count);
        Assert.EndsWith("¿Para qué día?", modelo.Llamadas[1][2]["content"]!.ToString());
    }

    [Fact]
    public async Task Error_de_herramienta_se_devuelve_al_modelo_sin_romper()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("consultar_disponibilidad", new { medico_id = "MED-99", fecha = "2026-10-27" })
            .ThenText("Ese médico no existe.");

        var (agent, _, _) = Crear(modelo);
        var reply = await agent.RunAsync(null, "cita", CancellationToken.None);

        Assert.Contains("\"error\"", reply.Pasos[0].Resultado);
        Assert.EndsWith("Ese médico no existe.", reply.Respuesta);
    }

    [Fact]
    public async Task Se_detiene_si_el_modelo_entra_en_bucle()
    {
        var modelo = new ScriptedChatModel();
        for (var i = 0; i < 10; i++)
            modelo.ThenTool("listar_medicos", new { });

        var (agent, _, _) = Crear(modelo);
        var reply = await agent.RunAsync(null, "hola", CancellationToken.None);

        Assert.Equal(8, reply.Pasos.Count);
        Assert.Contains("personal del consultorio", reply.Respuesta);
    }
}
