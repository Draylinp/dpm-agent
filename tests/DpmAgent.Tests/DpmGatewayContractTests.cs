using DpmAgent.Api.Dpm;

namespace DpmAgent.Tests;

/// <summary>
/// Comportamiento que debe cumplir cualquier <see cref="IDpmGateway"/>.
/// Se ejecuta contra el simulado en memoria y contra la base de datos real.
/// </summary>
public abstract class DpmGatewayContractTests
{
    // Lunes 26 de octubre de 2026, 12:00 p. m. en República Dominicana.
    protected static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    protected static readonly DateOnly Martes = new(2026, 10, 27);

    protected abstract IDpmGateway Crear();

    private static DateTimeOffset MartesA(int hora) => HorarioConsultorio.EnHoraRd(Martes, new TimeOnly(hora, 0));

    [Fact]
    public async Task Lista_los_medicos_ficticios()
    {
        var medicos = await Crear().ListarMedicosAsync(CancellationToken.None);

        Assert.Equal(["MED-01", "MED-02"], medicos.Select(m => m.Id));
        Assert.All(medicos, m => Assert.Contains("(fictici", m.Nombre));
    }

    [Fact]
    public async Task Fin_de_semana_no_tiene_horarios()
    {
        var slots = await Crear().ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 10, 31), CancellationToken.None);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task Dia_habil_tiene_14_horarios_de_30_minutos()
    {
        var slots = await Crear().ConsultarDisponibilidadAsync("MED-01", Martes, CancellationToken.None);

        Assert.Equal(14, slots.Count);
        Assert.Equal(MartesA(8), slots[0]);
    }

    [Fact]
    public async Task Hoy_solo_muestra_horarios_futuros()
    {
        var slots = await Crear().ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.All(slots, s => Assert.True(s > Ahora));
        Assert.Equal(6, slots.Count); // 14:00 a 16:30
    }

    [Fact]
    public async Task Medico_inexistente_es_un_error()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Crear().ConsultarDisponibilidadAsync("MED-99", Martes, CancellationToken.None));
    }

    [Fact]
    public async Task No_permite_reservar_dos_veces_el_mismo_horario()
    {
        var dpm = Crear();
        await dpm.CrearCitaAsync("PAC-001", "MED-01", MartesA(9), "control", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dpm.CrearCitaAsync("PAC-002", "MED-01", MartesA(9), "control", CancellationToken.None));
    }

    [Fact]
    public async Task No_permite_reservar_en_el_pasado()
    {
        var pasado = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 26), new TimeOnly(9, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Crear().CrearCitaAsync("PAC-001", "MED-01", pasado, "control", CancellationToken.None));
    }

    [Fact]
    public async Task Otro_medico_si_puede_usar_el_mismo_horario()
    {
        var dpm = Crear();
        await dpm.CrearCitaAsync("PAC-001", "MED-01", MartesA(9), "control", CancellationToken.None);

        var cita = await dpm.CrearCitaAsync("PAC-002", "MED-02", MartesA(9), "control", CancellationToken.None);
        Assert.Equal("MED-02", cita.MedicoId);
    }

    [Fact]
    public async Task Cancelar_libera_el_horario()
    {
        var dpm = Crear();
        var cita = await dpm.CrearCitaAsync("PAC-001", "MED-01", MartesA(9), "control", CancellationToken.None);
        Assert.DoesNotContain(MartesA(9), await dpm.ConsultarDisponibilidadAsync("MED-01", Martes, CancellationToken.None));

        var cancelada = await dpm.CancelarCitaAsync(cita.Id, "PAC-001", CancellationToken.None);

        Assert.Equal("cancelada", cancelada?.Estado);
        Assert.Contains(MartesA(9), await dpm.ConsultarDisponibilidadAsync("MED-01", Martes, CancellationToken.None));
        Assert.Empty(await dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None));

        // El horario liberado se puede volver a reservar.
        await dpm.CrearCitaAsync("PAC-002", "MED-01", MartesA(9), "control", CancellationToken.None);
    }

    [Fact]
    public async Task No_cancela_la_cita_de_otro_paciente()
    {
        var dpm = Crear();
        var cita = await dpm.CrearCitaAsync("PAC-002", "MED-01", MartesA(9), "control", CancellationToken.None);

        Assert.Null(await dpm.CancelarCitaAsync(cita.Id, "PAC-001", CancellationToken.None));
        Assert.Null(await dpm.CancelarCitaAsync("CITA-999999", "PAC-001", CancellationToken.None));
        Assert.Single(await dpm.CitasDePacienteAsync("PAC-002", CancellationToken.None));
    }

    [Fact]
    public async Task Citas_del_paciente_en_orden_y_en_hora_de_rd()
    {
        var dpm = Crear();
        await dpm.CrearCitaAsync("PAC-001", "MED-01", MartesA(15), "control", CancellationToken.None);
        await dpm.CrearCitaAsync("PAC-001", "MED-02", MartesA(9), "control", CancellationToken.None);

        var citas = await dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None);

        Assert.Equal([MartesA(9), MartesA(15)], citas.Select(c => c.FechaHora));
        Assert.All(citas, c => Assert.Equal("PAC-001", c.PacienteId));
    }

    [Fact]
    public async Task Busca_paciente_por_telefono_con_formato_o_por_id()
    {
        var dpm = Crear();

        Assert.Equal("PAC-001", (await dpm.BuscarPacienteAsync("(809) 555-0101", CancellationToken.None))?.Id);
        Assert.Equal("PAC-001", (await dpm.BuscarPacienteAsync("+1 809 555 0101", CancellationToken.None))?.Id);
        Assert.Equal("PAC-002", (await dpm.BuscarPacienteAsync("pac-002", CancellationToken.None))?.Id);
        Assert.Null(await dpm.BuscarPacienteAsync("8095559999", CancellationToken.None));
    }

    [Fact]
    public async Task Registra_paciente_con_telefono_normalizado()
    {
        var dpm = Crear();
        var p = await dpm.RegistrarPacienteAsync("Rosa Peña (ficticia)", "849-555-0123", " ARS Ejemplo ", CancellationToken.None);

        Assert.Equal("8495550123", p.Telefono);
        Assert.Equal("ARS Ejemplo", p.Ars);
        Assert.Equal(p.Id, (await dpm.BuscarPacienteAsync("8495550123", CancellationToken.None))?.Id);
    }

    [Fact]
    public async Task Registra_y_lista_escalamientos_del_mas_reciente_al_mas_antiguo()
    {
        var dpm = Crear();
        await dpm.RegistrarEscalamientoAsync("s1", "PAC-001", "Pregunta sobre un resultado", false, CancellationToken.None);
        var urgente = await dpm.RegistrarEscalamientoAsync("s2", null, "Posible emergencia", true, CancellationToken.None);

        var lista = await dpm.ListarEscalamientosAsync(CancellationToken.None);

        Assert.Equal(2, lista.Count);
        Assert.Equal(urgente.Id, lista[0].Id);
        Assert.True(lista[0].Urgente);
        Assert.Equal("pendiente", lista[0].Estado);
        Assert.Equal("PAC-001", lista[1].PacienteId);
    }
    [Fact]
    public async Task Guarda_y_resuelve_carnets_pendientes()
    {
        var dpm = Crear();
        var primero = await dpm.GuardarCarnetPendienteAsync("s1", "8095550101", "PAC-001",
            new DatosCarnet("María Rodríguez", "ARS Ejemplo", "AF-1"), CancellationToken.None);
        var segundo = await dpm.GuardarCarnetPendienteAsync("s1", "8095550101", null,
            new DatosCarnet("María Rodríguez", "ARS Ejemplo", "AF-2"), CancellationToken.None);
        await dpm.GuardarCarnetPendienteAsync("s2", "8295550102", null,
            new DatosCarnet(null, "ARS Prueba", null), CancellationToken.None);

        Assert.Equal("pendiente", primero.Estado);
        Assert.Equal("PAC-001", primero.PacienteId);
        Assert.Equal(segundo.Id, (await dpm.UltimoCarnetPendienteAsync("s1", CancellationToken.None))?.Id);
        Assert.Equal(3, (await dpm.ListarCarnetsPendientesAsync(CancellationToken.None)).Count);

        var resuelto = await dpm.ResolverCarnetAsync(segundo.Id, "confirmado",
            new DatosCarnet("María Rodríguez", "ARS Ejemplo", "AF-22"), "PAC-001", "paciente", CancellationToken.None);

        Assert.Equal("confirmado", resuelto.Estado);
        Assert.Equal("AF-22", resuelto.Datos.NumeroAfiliado);
        Assert.Equal("paciente", resuelto.ResueltoPor);
        Assert.Equal(primero.Id, (await dpm.UltimoCarnetPendienteAsync("s1", CancellationToken.None))?.Id);
        Assert.Equal("confirmado", (await dpm.ObtenerCarnetAsync(segundo.Id, CancellationToken.None))?.Estado);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dpm.ResolverCarnetAsync(
            segundo.Id, "descartado", resuelto.Datos, null, "secretaria", CancellationToken.None));
    }

    [Fact]
    public async Task Actualizar_seguro_no_borra_lo_que_no_viene()
    {
        var dpm = Crear();
        await dpm.ActualizarSeguroAsync("PAC-001", null, "AF-555", CancellationToken.None);

        var p = await dpm.BuscarPacienteAsync("PAC-001", CancellationToken.None);
        Assert.Equal("ARS Ejemplo", p!.Ars);
        Assert.Equal("AF-555", p.NumeroAfiliado);
        await Assert.ThrowsAsync<ArgumentException>(() => dpm.ActualizarSeguroAsync("PAC-999", "X", null, CancellationToken.None));
    }
}

public sealed class MockDpmGatewayTests : DpmGatewayContractTests
{
    protected override IDpmGateway Crear() => new MockDpmGateway(new FixedClock(Ahora));
}

public sealed class DbDpmGatewayTests : DpmGatewayContractTests, IDisposable
{
    private readonly SqliteDePrueba _db = new(new FixedClock(Ahora));

    protected override IDpmGateway Crear() => new DbDpmGateway(_db.Contexto(), new FixedClock(Ahora));

    public void Dispose() => _db.Dispose();
}
