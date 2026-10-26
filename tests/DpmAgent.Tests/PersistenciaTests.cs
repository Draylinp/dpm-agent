using System.Text.Json.Nodes;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

public class PersistenciaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly FixedClock Reloj = new(Ahora);
    private static readonly DateTimeOffset Martes9 = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));

    [Fact]
    public async Task Un_consultorio_no_ve_los_datos_de_otro()
    {
        using var db = new SqliteDePrueba(Reloj);
        var ctx1 = db.Contexto(1);
        var cita = await new DbDpmGateway(ctx1, Reloj).CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);

        var ctx2 = db.Contexto(2);
        ctx2.Consultorios.Add(new E.Consultorio { Nombre = "Otro consultorio (ficticio)", CreadoUtc = Ahora.UtcDateTime });
        await ctx2.SaveChangesAsync();
        var otro = new DbDpmGateway(ctx2, Reloj);

        Assert.Empty(await otro.ListarMedicosAsync(CancellationToken.None));
        Assert.Null(await otro.BuscarPacienteAsync("8095550101", CancellationToken.None));
        Assert.Null(await otro.BuscarPacienteAsync("PAC-001", CancellationToken.None));
        Assert.Empty(await otro.CitasDePacienteAsync("PAC-001", CancellationToken.None));
        Assert.Null(await otro.CancelarCitaAsync(cita.Id, "PAC-001", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => otro.ConsultarDisponibilidadAsync("MED-01", new DateOnly(2026, 10, 27), CancellationToken.None));
    }

    [Fact]
    public async Task La_base_rechaza_una_doble_reserva_aunque_se_salte_la_validacion()
    {
        using var db = new SqliteDePrueba(Reloj);
        var ctx = db.Contexto();
        await new DbDpmGateway(ctx, Reloj).CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);

        ctx.Citas.Add(new E.Cita
        {
            ConsultorioId = 1, PacienteId = 2, MedicoId = 1, FechaHoraUtc = Martes9.UtcDateTime,
            Motivo = "control", Estado = "confirmada", CreadaUtc = Ahora.UtcDateTime,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task El_historial_guarda_solo_los_mensajes_nuevos_y_el_paciente()
    {
        using var db = new SqliteDePrueba(Reloj);

        var store = new DbConversationStore(db.Contexto(), Reloj);
        var sesion = await store.ObtenerAsync("wa-1", CancellationToken.None);
        sesion.VincularTelefono("8095550101");
        sesion.PacienteId = "PAC-001";
        sesion.Mensajes.Add(new JsonObject { ["role"] = "user", ["content"] = "hola" });
        sesion.Mensajes.Add(new JsonObject { ["role"] = "assistant", ["content"] = "¿En qué le ayudo?" });
        await store.GuardarAsync(sesion, CancellationToken.None);

        sesion.Mensajes.Add(new JsonObject { ["role"] = "user", ["content"] = "una cita" });
        await store.GuardarAsync(sesion, CancellationToken.None);

        var leida = await new DbConversationStore(db.Contexto(), Reloj).ObtenerAsync("wa-1", CancellationToken.None);
        Assert.Equal("8095550101", leida.TelefonoCanal);
        Assert.Equal("PAC-001", leida.PacienteId);
        Assert.Equal(["hola", "¿En qué le ayudo?", "una cita"], leida.Mensajes.Select(m => m["content"]!.ToString()));
        Assert.Equal(3, await db.Contexto().Mensajes.CountAsync());
    }

    [Fact]
    public async Task Al_reiniciar_la_app_se_conservan_citas_conversaciones_y_escalamientos()
    {
        var archivo = Path.Combine(Path.GetTempPath(), $"dpm-prueba-{Guid.NewGuid():N}.db");
        try
        {
            // Primer arranque: María se identifica, reserva y pide hablar con alguien.
            var modelo1 = new ScriptedChatModel()
                .ThenTool("buscar_paciente", new { })
                .ThenTool("crear_cita", new { medico_id = "MED-01", fecha_hora = "2026-10-27T09:00:00-04:00", motivo = "control" })
                .ThenTool("escalar_a_personal", new { motivo = "Quiere hablar con la secretaria", urgente = false })
                .ThenText("Listo, su cita quedó el martes a las 9:00 a. m.");

            string sesionId;
            await using (var app1 = await ArrancarAsync(archivo, modelo1))
            {
                await using var scope = app1.CreateAsyncScope();
                var r = await scope.ServiceProvider.GetRequiredService<ReceptionAgent>()
                    .RunAsync(null, "Quiero cita el martes a las 9", "809-555-0101", CancellationToken.None);
                sesionId = r.SesionId;
                Assert.Contains("\"creada\":true", r.Pasos[1].Resultado);
            }

            SqliteConnection.ClearAllPools();

            // Segundo arranque, mismo archivo: la sesión recuerda al paciente y el historial.
            var modelo2 = new ScriptedChatModel()
                .ThenTool("citas_del_paciente", new { })
                .ThenText("Tiene una cita el martes a las 9:00 a. m.");

            await using var app2 = await ArrancarAsync(archivo, modelo2);
            await using var scope2 = app2.CreateAsyncScope();
            var sp = scope2.ServiceProvider;

            var r2 = await sp.GetRequiredService<ReceptionAgent>().RunAsync(sesionId, "¿Qué citas tengo?", null, CancellationToken.None);

            Assert.Contains("CITA-1", r2.Pasos[0].Resultado);
            Assert.DoesNotContain("\"error\"", r2.Pasos[0].Resultado);
            // system + 8 mensajes del primer turno + el nuevo mensaje del usuario
            Assert.Equal(10, modelo2.Llamadas[0].Count);
            Assert.Single(await sp.GetRequiredService<IDpmGateway>().ListarEscalamientosAsync(CancellationToken.None));
            Assert.Equal(2, await sp.GetRequiredService<DpmDbContext>().Pacientes.CountAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(archivo);
        }
    }

    [Fact]
    public void Proveedor_desconocido_es_un_error_de_configuracion()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BaseDatos:Proveedor"] = "Oracle" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPersistencia(config));
    }

    [Fact]
    public void Postgres_sin_cadena_de_conexion_es_un_error_de_configuracion()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BaseDatos:Proveedor"] = "Postgres" })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddPersistencia(config));
        Assert.Contains("ConnectionStrings__Dpm", ex.Message);
    }

    /// <summary>Arma los servicios igual que Program.cs, con el modelo falso.</summary>
    internal static async Task<ServiceProvider> ArrancarAsync(string archivoSqlite, IChatModel modelo, string proveedor = "Sqlite", string? cadena = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseDatos:Proveedor"] = proveedor,
                ["ConnectionStrings:Dpm"] = cadena ?? $"Data Source={archivoSqlite}",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Reloj);
        services.AddSingleton(modelo);
        services.AddPersistencia(config);
        services.AddScoped<AgentTools>();
        services.AddScoped<ReceptionAgent>();

        var sp = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await sp.InicializarBaseDatosAsync();
        return sp;
    }
}
