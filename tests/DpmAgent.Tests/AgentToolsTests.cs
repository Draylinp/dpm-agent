using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Tests;

public class AgentToolsTests
{
    private static AgentTools Crear() =>
        new(new MockDpmGateway(new FixedClock(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4)))));

    [Fact]
    public void Define_dieciseis_herramientas_sin_orientacion_clinica()
    {
        var nombres = Crear().Definitions.Select(t => t!["function"]!["name"]!.ToString()).ToList();

        Assert.Equal(16, nombres.Count);
        Assert.Contains("escalar_a_personal", nombres);
        Assert.DoesNotContain(nombres, n => n.Contains("diagnost") || n.Contains("medicamento"));
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
