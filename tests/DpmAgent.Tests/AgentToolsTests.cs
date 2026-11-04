using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Tests;

public class AgentToolsTests
{
    private static AgentTools Crear() =>
        new(new MockDpmGateway(new FixedClock(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4)))));

    [Fact]
    public void Define_veintiuna_herramientas_sin_orientacion_clinica()
    {
        var nombres = Crear().Definitions.Select(t => t!["function"]!["name"]!.ToString()).ToList();

        Assert.Equal(21, nombres.Count);
        Assert.Contains("escalar_a_personal", nombres);
        Assert.DoesNotContain(nombres, n => n.Contains("diagnost") || n.Contains("medicamento") || n.Contains("sintoma"));
    }

    [Theory]
    [InlineData("""{"resumen_es":"Dolor en el pecho","resumen_en":"Chest pain","urgente":true}""")]
    [InlineData("""{"resumen_es":"Dolor en el pecho","resumen_en":"Chest pain","urgente":"true"}""")]
    public async Task Una_emergencia_es_urgente_aunque_el_modelo_mande_el_booleano_como_texto(string args)
    {
        var sesion = new Sesion("s");
        var r = await Crear().ExecuteAsync(sesion, "escalar_a_personal", args, CancellationToken.None);
        Assert.Contains("\"urgente\":true", r);
    }

    [Fact]
    public void Lee_booleanos_y_listas_escritos_como_texto()
    {
        var a = System.Text.Json.Nodes.JsonNode.Parse("""{"x":"false","y":"sí","z":"quizás","c":"[\"recordatorios\"]","d":"[]"}""")!.AsObject();
        Assert.Equal((false, true, (bool?)null), (AgentTools.Bool(a, "x"), AgentTools.Bool(a, "y"), AgentTools.Bool(a, "z")));
        Assert.Equal(["recordatorios"], AgentTools.Lista(a, "c"));
        Assert.Null(AgentTools.Lista(a, "d"));
    }

    [Fact]
    public async Task Parametro_faltante_devuelve_error()
    {
        var r = await Crear().ExecuteAsync(new Sesion("s"), "consultar_disponibilidad", "{}", CancellationToken.None);
        Assert.Contains("Falta el parámetro 'medico_id'", r);
    }

    [Fact]
    public async Task Herramienta_desconocida_devuelve_error()
    {
        var r = await Crear().ExecuteAsync(new Sesion("s"), "recetar", "{}", CancellationToken.None);
        Assert.Contains("Herramienta desconocida", r);
    }

    [Fact]
    public async Task Json_invalido_devuelve_error()
    {
        var r = await Crear().ExecuteAsync(new Sesion("s"), "listar_medicos", "{no-json", CancellationToken.None);
        Assert.Contains("no son JSON válido", r);
    }

    [Fact]
    public async Task Registra_y_luego_encuentra_un_paciente_por_el_telefono_del_canal()
    {
        var tools = Crear();
        var sesion = new Sesion("s");
        sesion.VincularTelefono("+1 849-555-0199");

        var reg = await tools.ExecuteAsync(sesion, "registrar_paciente",
            """{"nombre":"Pedro Gómez (ficticio)","ars":"ARS Ejemplo"}""", CancellationToken.None);
        Assert.Contains("\"registrado\":true", reg);

        var otraSesion = new Sesion("s2");
        otraSesion.VincularTelefono("849 555 0199");
        var r = await tools.ExecuteAsync(otraSesion, "buscar_paciente", "{}", CancellationToken.None);

        Assert.Contains("\"encontrado\":true", r);
        Assert.Contains("Pedro Gómez", r);
        Assert.Equal(sesion.PacienteId, otraSesion.PacienteId);
    }
}
