using DpmAgent.Api.Agent;
using DpmAgent.Api.Data;
using DpmAgent.Api.Demo;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

/// <summary>Solo corre si existe la variable DPM_TEST_POSTGRES con una cadena de conexión a una base desechable.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public static string? Cadena => Environment.GetEnvironmentVariable("DPM_TEST_POSTGRES");

    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Cadena))
            Skip = "Defina DPM_TEST_POSTGRES para probar contra PostgreSQL.";
    }
}

public class PostgresTests
{
    [PostgresFact]
    public async Task Migraciones_y_flujo_completo_en_postgres()
    {
        var martes9 = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("crear_cita", new { medico_id = "MED-01", fecha_hora = "2026-10-27T09:00:00-04:00", motivo = "control" })
            .ThenText("Listo");

        await using (var limpiar = await PersistenciaTests.ArrancarAsync("", modelo, "Postgres", PostgresFactAttribute.Cadena))
        {
            var db = limpiar.CreateScope().ServiceProvider.GetRequiredService<DpmDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await using var sp = await PersistenciaTests.ArrancarAsync("", modelo, "Postgres", PostgresFactAttribute.Cadena);
        await using var scope = sp.CreateAsyncScope();

        var r = await scope.ServiceProvider.GetRequiredService<ReceptionAgent>()
            .RunAsync(null, "cita el martes a las 9", "8095550101", CancellationToken.None);
        Assert.Contains("\"creada\":true", r.Pasos[1].Resultado);

        await using var otroScope = sp.CreateAsyncScope();
        var dpm = otroScope.ServiceProvider.GetRequiredService<IDpmGateway>();
        Assert.Equal(martes9, Assert.Single(await dpm.CitasDePacienteAsync("PAC-001", CancellationToken.None)).FechaHora);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dpm.CrearCitaAsync("PAC-002", "MED-01", martes9, "control", CancellationToken.None));

        var sesion = await otroScope.ServiceProvider.GetRequiredService<IConversationStore>().ObtenerAsync(r.SesionId, CancellationToken.None);
        Assert.Equal("PAC-001", sesion.PacienteId);
        Assert.Equal(6, sesion.Mensajes.Count);

        // Reinicio de la demo: borra en orden (claves foráneas reales de PostgreSQL) y vuelve a sembrar.
        await using var demoScope = sp.CreateAsyncScope();
        var demo = ActivatorUtilities.CreateInstance<ServicioDemo>(demoScope.ServiceProvider, Options.Create(new DemoOptions { Habilitado = true }));
        await demo.ReiniciarAsync(CancellationToken.None);
        var dbDemo = demoScope.ServiceProvider.GetRequiredService<DpmDbContext>();
        Assert.Equal(2, await dbDemo.Pacientes.CountAsync());
        Assert.Equal(2, await dbDemo.Conversaciones.CountAsync());
        Assert.Equal(1, await dbDemo.Citas.CountAsync());
    }
}
