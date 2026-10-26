using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Tests;

public class AgentToolsTests
{
    private static AgentTools Crear() =>
        new(new MockDpmGateway(new FixedClock(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4)))));

    [Fact]
    public void Define_ocho_herramientas_sin_orientacion_clinica()
    {
        var nombres = Crear().Definitions.Select(t => t!["function"]!["name"]!.ToString()).ToList();

        Assert.Equal(8, nombres.Count);
        Assert.Contains("escalar_a_personal", nombres);
        Assert.DoesNotContain(nombres, n => n.Contains("diagnost") || n.Contains("medicamento"));
    }

    [Fact]
    public async Task Parametro_faltante_devuelve_error()
    {
        var r = await Crear().ExecuteAsync("s", "buscar_paciente", "{}", CancellationToken.None);
        Assert.Contains("Falta el parámetro 'telefono_o_id'", r);
    }

    [Fact]
    public async Task Herramienta_desconocida_devuelve_error()
    {
        var r = await Crear().ExecuteAsync("s", "recetar", "{}", CancellationToken.None);
        Assert.Contains("Herramienta desconocida", r);
    }

    [Fact]
    public async Task Json_invalido_devuelve_error()
    {
        var r = await Crear().ExecuteAsync("s", "listar_medicos", "{no-json", CancellationToken.None);
        Assert.Contains("no son JSON válido", r);
    }

    [Fact]
    public async Task Registra_y_luego_encuentra_un_paciente()
    {
        var tools = Crear();
        await tools.ExecuteAsync("s", "registrar_paciente",
            """{"nombre":"Pedro Gómez (ficticio)","telefono":"849-555-0199","ars":"ARS Ejemplo"}""",
            CancellationToken.None);

        var r = await tools.ExecuteAsync("s", "buscar_paciente", """{"telefono_o_id":"8495550199"}""", CancellationToken.None);
        Assert.Contains("\"encontrado\":true", r);
        Assert.Contains("Pedro Gómez", r);
    }
}
