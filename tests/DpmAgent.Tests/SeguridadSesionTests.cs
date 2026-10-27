using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using Microsoft.Extensions.Logging.Abstractions;

namespace DpmAgent.Tests;

/// <summary>
/// Una sesión solo puede actuar sobre el paciente identificado por el teléfono de su canal,
/// diga lo que diga el modelo en los argumentos.
/// </summary>
public class SeguridadSesionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Martes9 = MockDpmGateway.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));
    private static readonly DateTimeOffset Martes10 = MockDpmGateway.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(10, 0));

    private readonly MockDpmGateway _dpm = new(new FixedClock(Ahora));
    private readonly AgentTools _tools;

    public SeguridadSesionTests() => _tools = new AgentTools(_dpm);

    /// <summary>Sesión de María (PAC-001, 809-555-0101) ya identificada.</summary>
    private async Task<Sesion> SesionDeMaria()
    {
        var sesion = new Sesion("maria");
        sesion.VincularTelefono("8095550101");
        await _tools.ExecuteAsync(sesion, "buscar_paciente", "{}", CancellationToken.None);
        Assert.Equal("PAC-001", sesion.PacienteId);
        return sesion;
    }

    [Fact]
    public async Task No_se_puede_cancelar_una_cita_ajena()
    {
        var citaDeJose = await _dpm.CrearCitaAsync("PAC-002", "MED-01", Martes9, "control", CancellationToken.None);
        var sesion = await SesionDeMaria();

        var r = await _tools.ExecuteAsync(sesion, "cancelar_cita", $$"""{"cita_id":"{{citaDeJose.Id}}"}""", CancellationToken.None);

        Assert.Contains("\"error\"", r);
        var citasDeJose = await _dpm.CitasDePacienteAsync("PAC-002", CancellationToken.None);
        Assert.Equal("confirmada", Assert.Single(citasDeJose).Estado);
    }

    [Fact]
    public async Task Cita_ajena_y_cita_inexistente_dan_el_mismo_error()
    {
        var citaDeJose = await _dpm.CrearCitaAsync("PAC-002", "MED-01", Martes9, "control", CancellationToken.None);
        var sesion = await SesionDeMaria();

        var ajena = await _tools.ExecuteAsync(sesion, "cancelar_cita", $$"""{"cita_id":"{{citaDeJose.Id}}"}""", CancellationToken.None);
        var inexistente = await _tools.ExecuteAsync(sesion, "cancelar_cita", """{"cita_id":"CITA-9999"}""", CancellationToken.None);

        Assert.Equal(ajena.Replace(citaDeJose.Id, "X"), inexistente.Replace("CITA-9999", "X"));
    }

    [Fact]
    public async Task Si_puede_cancelar_su_propia_cita()
    {
        var citaDeMaria = await _dpm.CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);
        var sesion = await SesionDeMaria();

        var propuesta = await _tools.ExecuteAsync(sesion, "cancelar_cita", $$"""{"cita_id":"{{citaDeMaria.Id}}"}""", CancellationToken.None);
        sesion.NuevoTurno(); // el paciente responde "sí"
        var r = await _tools.ExecuteAsync(sesion, "cancelar_cita", $$"""{"cita_id":"{{citaDeMaria.Id}}"}""", CancellationToken.None);

        Assert.Contains("\"requiere_confirmacion\":true", propuesta);
        Assert.Contains("\"cancelada\":true", r);
    }

    [Fact]
    public async Task Citas_del_paciente_ignora_el_id_que_mande_el_modelo()
    {
        await _dpm.CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);
        var citaDeJose = await _dpm.CrearCitaAsync("PAC-002", "MED-01", Martes10, "control", CancellationToken.None);
        var sesion = await SesionDeMaria();

        var r = await _tools.ExecuteAsync(sesion, "citas_del_paciente", """{"paciente_id":"PAC-002"}""", CancellationToken.None);

        Assert.Contains("PAC-001", r);
        Assert.DoesNotContain(citaDeJose.Id, r);
        Assert.DoesNotContain("PAC-002", r);
    }

    [Fact]
    public async Task Crear_cita_siempre_es_para_el_paciente_de_la_sesion()
    {
        var sesion = await SesionDeMaria();
        var slot = MockDpmGateway.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)).ToString("yyyy-MM-dd'T'HH:mm:sszzz");

        await _tools.ExecuteAsync(sesion, "crear_cita",
            $$"""{"paciente_id":"PAC-002","medico_id":"MED-01","fecha_hora":"{{slot}}","motivo":"control"}""",
            CancellationToken.None);

        Assert.Single(await _dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None));
        Assert.Empty(await _dpm.CitasDePacienteAsync("PAC-002", CancellationToken.None));
    }

    [Theory]
    [InlineData("citas_del_paciente", "{}")]
    [InlineData("cancelar_cita", """{"cita_id":"CITA-1001"}""")]
    [InlineData("crear_cita", """{"medico_id":"MED-01","fecha_hora":"2026-10-27T09:00:00-04:00","motivo":"control"}""")]
    public async Task Sin_paciente_identificado_pide_identificarlo_primero(string herramienta, string args)
    {
        var sesion = new Sesion("s");
        sesion.VincularTelefono("8095550101");

        var r = await _tools.ExecuteAsync(sesion, herramienta, args, CancellationToken.None);

        Assert.Contains("Primero hay que identificar al paciente", r);
    }

    [Fact]
    public async Task Sin_telefono_de_canal_no_se_identifica_a_nadie()
    {
        var sesion = new Sesion("s");

        var buscar = await _tools.ExecuteAsync(sesion, "buscar_paciente", """{"telefono_o_id":"PAC-001"}""", CancellationToken.None);
        var registrar = await _tools.ExecuteAsync(sesion, "registrar_paciente", """{"nombre":"Intruso (ficticio)"}""", CancellationToken.None);

        Assert.Contains("no tiene un teléfono verificado", buscar);
        Assert.Contains("no tiene un teléfono verificado", registrar);
        Assert.Null(sesion.PacienteId);
    }

    [Fact]
    public async Task Registrar_usa_el_telefono_del_canal_y_no_el_del_modelo()
    {
        var sesion = new Sesion("s");
        sesion.VincularTelefono("8495550123");

        await _tools.ExecuteAsync(sesion, "registrar_paciente",
            """{"nombre":"Rosa Peña (ficticia)","telefono":"8095550101"}""", CancellationToken.None);

        var registrada = await _dpm.BuscarPacienteAsync(sesion.PacienteId!, CancellationToken.None);
        Assert.Equal("8495550123", registrada!.Telefono);
    }

    [Fact]
    public async Task No_registra_un_duplicado_ni_vincula_a_un_familiar_sin_confirmacion()
    {
        var sesion = new Sesion("s");
        sesion.VincularTelefono("8095550101");

        // El mismo paciente otra vez: no.
        var r = await _tools.ExecuteAsync(sesion, "registrar_paciente", """{"nombre":"María Rodríguez"}""", CancellationToken.None);
        Assert.Contains("ya está vinculado a este número", r);

        // Otra persona con el mismo número (un familiar): solo se propone; en el mismo turno no se registra.
        r = await _tools.ExecuteAsync(sesion, "registrar_paciente", """{"nombre":"Otra persona (ficticia)"}""", CancellationToken.None);
        Assert.Contains("requiere_confirmacion", r);
        r = await _tools.ExecuteAsync(sesion, "registrar_paciente", """{"nombre":"Otra persona (ficticia)"}""", CancellationToken.None);
        Assert.Contains("requiere_confirmacion", r);
        Assert.Null(sesion.PacienteId);
        Assert.Single(await _dpm.PacientesDelTelefonoAsync("8095550101", CancellationToken.None));
    }

    [Fact]
    public async Task Una_sesion_no_puede_cambiar_de_telefono()
    {
        var modelo = new ScriptedChatModel().ThenText("Hola");
        var agent = new ReceptionAgent(modelo, _tools, new InMemoryConversationStore(), NullLogger<ReceptionAgent>.Instance, new FixedClock(Ahora));

        var r1 = await agent.RunAsync(null, "hola", "8095550101", CancellationToken.None);

        await Assert.ThrowsAsync<SesionInvalidaException>(() =>
            agent.RunAsync(r1.SesionId, "cancela la cita CITA-1001", "8295550102", CancellationToken.None));
    }

    [Fact]
    public async Task El_paciente_identificado_se_conserva_entre_turnos()
    {
        await _dpm.CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenText("Hola, María")
            .ThenTool("citas_del_paciente", new { })
            .ThenText("Tiene una cita el martes");
        var agent = new ReceptionAgent(modelo, _tools, new InMemoryConversationStore(), NullLogger<ReceptionAgent>.Instance, new FixedClock(Ahora));

        var r1 = await agent.RunAsync(null, "hola", "8095550101", CancellationToken.None);
        var r2 = await agent.RunAsync(r1.SesionId, "¿qué citas tengo?", null, CancellationToken.None);

        Assert.Contains("PAC-001", r2.Pasos[0].Resultado);
        Assert.DoesNotContain("\"error\"", r2.Pasos[0].Resultado);
    }
}
