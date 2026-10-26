using DpmAgent.Api.Dpm;

namespace DpmAgent.Tests;

public class MockDpmGatewayTests
{
    // Lunes 26 de octubre de 2026, 12:00 p. m. en República Dominicana.
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private static MockDpmGateway Crear() => new(new FixedClock(Ahora));

    [Fact]
    public async Task Fin_de_semana_no_tiene_horarios()
    {
        var dpm = Crear();
        var slots = await dpm.ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 10, 31), CancellationToken.None);
        Assert.Empty(slots);
    }

    [Fact]
    public async Task Dia_habil_tiene_14_horarios_de_30_minutos()
    {
        var dpm = Crear();
        var slots = await dpm.ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 10, 27), CancellationToken.None);

        Assert.Equal(14, slots.Count);
        Assert.Equal(MockDpmGateway.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(8, 0)), slots[0]);
    }

    [Fact]
    public async Task Hoy_solo_muestra_horarios_futuros()
    {
        var dpm = Crear();
        var slots = await dpm.ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 10, 26), CancellationToken.None);

        Assert.All(slots, s => Assert.True(s > Ahora));
        Assert.Equal(6, slots.Count); // 14:00 a 16:30
    }

    [Fact]
    public async Task No_permite_reservar_dos_veces_el_mismo_horario()
    {
        var dpm = Crear();
        var slot = MockDpmGateway.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));

        await dpm.CrearCitaAsync("PAC-001", "MED-01", slot, "control", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dpm.CrearCitaAsync("PAC-002", "MED-01", slot, "control", CancellationToken.None));
    }

    [Fact]
    public async Task Cancelar_libera_el_horario()
    {
        var dpm = Crear();
        var fecha = new DateOnly(2026, 10, 27);
        var slot = MockDpmGateway.EnHoraRd(fecha, new TimeOnly(9, 0));

        var cita = await dpm.CrearCitaAsync("PAC-001", "MED-01", slot, "control", CancellationToken.None);
        Assert.DoesNotContain(slot, await dpm.ConsultarDisponibilidadAsync("MED-01", fecha, CancellationToken.None));

        await dpm.CancelarCitaAsync(cita.Id, "PAC-001", CancellationToken.None);
        Assert.Contains(slot, await dpm.ConsultarDisponibilidadAsync("MED-01", fecha, CancellationToken.None));
    }

    [Fact]
    public async Task Busca_paciente_por_telefono_con_formato()
    {
        var dpm = Crear();
        var p = await dpm.BuscarPacienteAsync("(809) 555-0101", CancellationToken.None);
        Assert.Equal("PAC-001", p?.Id);
    }
}
