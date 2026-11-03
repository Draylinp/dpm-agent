using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Tests;

/// <summary>F21: números que fallan, confirmación del número en seguimientos lejanos y pacientes desactivados.</summary>
public class ContactoTests
{
    private const string TelMaria = "8095550101";
    private static readonly DateTimeOffset Lunes = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private static (AppDePrueba App, RelojAjustable Reloj) Crear(ScriptedChatModel? modelo = null, Action<IServiceCollection>? extra = null)
    {
        var reloj = new RelojAjustable(Lunes);
        var app = new AppDePrueba(modelo, s =>
        {
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
            extra?.Invoke(s);
        });
        return (app, reloj);
    }

    private static Task<T> Db<T>(AppDePrueba app, Func<DpmDbContext, Task<T>> f) => app.EnAmbito(sp => f(sp.GetRequiredService<DpmDbContext>()));

    private static Task<ResultadoAviso> Avisar(AppDePrueba app, int paciente, string tipo = "recordatorio") =>
        app.EnAmbito(sp => sp.GetRequiredService<Notificador>().NotificarPacienteAsync(paciente,
            (i, c) => new MensajeAgenda($"Aviso de {c}. Para no recibir más avisos, responda STOP.", tipo, [], i), tipo, CancellationToken.None));

    /// <summary>Canal web que siempre falla, como un número que no existe en WhatsApp.</summary>
    private sealed class CanalQueFalla : IMessageChannel
    {
        public string Canal => CanalesConocidos.Web;

        public Task EnviarAsync(MensajeParaEnviar mensaje, CancellationToken ct) => throw new InvalidOperationException("número no existe");
    }

    [Fact]
    public async Task Tres_mensajes_fallidos_marcan_no_localizable_y_pasan_a_la_secretaria()
    {
        var (app, _) = Crear(extra: s =>
        {
            s.RemoveAll<IMessageChannel>();
            s.AddSingleton<IMessageChannel, CanalQueFalla>();
        });
        using var __ = app;
        await Db(app, async db =>
        {
            // Que los avisos salgan por el canal web de la prueba.
            db.Conversaciones.Add(new E.Conversacion { ConsultorioId = 1, SesionId = "web-maria", TelefonoCanal = TelMaria, PacienteId = 1, CreadaUtc = DateTime.UtcNow, ActualizadaUtc = DateTime.UtcNow });
            return await db.SaveChangesAsync();
        });

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await Avisar(app, 1)).Enviado);
            for (var intento = 0; intento < 5; intento++) // Agenda:MaxIntentosEnvio
                await app.EnAmbito(sp => sp.GetRequiredService<DespachadorMensajes>().DespacharPendientesAsync(CancellationToken.None));
        }

        var maria = await Db(app, db => db.Pacientes.SingleAsync(p => p.Id == 1));
        Assert.NotNull(maria.NoLocalizableUtc);
        Assert.Contains("no localizable", (await Db(app, db => db.Escalamientos.SingleAsync())).Motivo);

        // Ya no se le escribe: el siguiente aviso queda para llamar.
        Assert.False((await Avisar(app, 1)).Enviado);
        Assert.Equal("Número no localizable", (await Db(app, db => db.MensajesSalientes.OrderBy(m => m.Id).LastAsync())).Error);

        // Si vuelve a escribir, es localizable otra vez.
        await app.EnAmbito(async sp => { await sp.GetRequiredService<ServicioContacto>().MarcarLocalizableAsync(TelMaria, CancellationToken.None); return 0; });
        Assert.Null((await Db(app, db => db.Pacientes.SingleAsync(p => p.Id == 1))).NoLocalizableUtc);
    }

    [Fact]
    public async Task Un_seguimiento_lejano_confirma_primero_el_numero_y_un_no_lo_pasa_a_la_secretaria()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("confirmar_numero", new { correcto = false })
            .ThenText("Disculpe la molestia. Que tenga buen día.");
        var (app, reloj) = Crear(modelo);
        using var __ = app;
        // Indicado hace un año para hoy + 21 días (el contacto se abre hoy).
        await Db(app, async db =>
        {
            db.Seguimientos.Add(new E.Seguimiento
            {
                ConsultorioId = 1, PacienteId = 1, MedicoId = 1, FechaObjetivo = new DateOnly(2026, 11, 16), ToleranciaDias = 7, Motivo = "control",
                Estado = ServicioSeguimientos.Pendiente, CreadoPor = "prueba", CreadoUtc = new DateTime(2025, 11, 16, 12, 0, 0, DateTimeKind.Utc),
            });
            return await db.SaveChangesAsync();
        });
        await app.EnAmbito(sp => sp.GetRequiredService<ServicioSeguimientos>().ProcesarAsync(CancellationToken.None));

        var aviso = await Db(app, db => db.MensajesSalientes.SingleAsync(m => m.Tipo == "seguimiento"));
        Assert.Equal("seguimiento_confirmar_numero", aviso.Plantilla);
        Assert.Contains("¿Este sigue siendo su número?", aviso.Texto);
        Assert.DoesNotContain("Valdez", aviso.Texto); // sin datos del médico hasta confirmar

        var r = await app.EnAmbito(sp => sp.GetRequiredService<ReceptionAgent>().RunAsync("wa-" + TelMaria, "NO, no conozco a esa persona", TelMaria, CancellationToken.None));
        Assert.Contains("\"confirmado\":false", r.Pasos[1].Resultado);
        var (seg, paciente, esc) = await Db(app, async db => (await db.Seguimientos.SingleAsync(), await db.Pacientes.SingleAsync(p => p.Id == 1), await db.Escalamientos.SingleAsync()));
        Assert.Equal(ServicioSeguimientos.SinRespuesta, seg.Estado);
        Assert.NotNull(paciente.NoLocalizableUtc);
        Assert.Contains("ya no es del paciente", esc.Motivo);
    }

    [Fact]
    public async Task Un_seguimiento_cercano_no_pide_confirmar_el_numero()
    {
        var (app, _) = Crear();
        using var __ = app;
        await Db(app, async db =>
        {
            db.Seguimientos.Add(new E.Seguimiento
            {
                ConsultorioId = 1, PacienteId = 1, MedicoId = 1, FechaObjetivo = new DateOnly(2026, 11, 16), ToleranciaDias = 7, Motivo = "control",
                Estado = ServicioSeguimientos.Pendiente, CreadoPor = "prueba", CreadoUtc = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
            });
            return await db.SaveChangesAsync();
        });
        await app.EnAmbito(sp => sp.GetRequiredService<ServicioSeguimientos>().ProcesarAsync(CancellationToken.None));
        Assert.Equal("seguimiento_contacto", (await Db(app, db => db.MensajesSalientes.SingleAsync())).Plantilla);
    }

    [Fact]
    public async Task Un_paciente_desactivado_no_recibe_nada_ni_se_identifica_por_su_numero()
    {
        var (app, _) = Crear();
        using var __ = app;
        await app.IniciarSesionAsync();
        await Db(app, async db =>
        {
            db.Seguimientos.Add(new E.Seguimiento
            {
                ConsultorioId = 1, PacienteId = 1, MedicoId = 1, FechaObjetivo = new DateOnly(2027, 4, 26), ToleranciaDias = 7, Motivo = "control",
                Estado = ServicioSeguimientos.Pendiente, CreadoPor = "prueba", CreadoUtc = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.PostAsync("/api/panel/pacientes/PAC-001/desactivar", null)).StatusCode);
        Assert.Equal(ServicioSeguimientos.Cancelado, (await Db(app, db => db.Seguimientos.SingleAsync())).Estado);

        Assert.False((await Avisar(app, 1)).Enviado);
        Assert.Equal(0, await Db(app, db => db.MensajesSalientes.CountAsync()));
        Assert.Null(await app.ConGateway(g => g.BuscarPacienteAsync(TelMaria, CancellationToken.None)));
        Assert.Empty(await app.ConGateway(g => g.PacientesDelTelefonoAsync(TelMaria, CancellationToken.None)));

        var familia = (await app.Cliente.GetFromJsonAsync<JsonArray>($"/api/panel/familias?telefono={TelMaria}"))!;
        Assert.False(Assert.Single(familia)!["activo"]!.GetValue<bool>());

        Assert.Equal(HttpStatusCode.OK, (await app.Cliente.PostAsync("/api/panel/pacientes/PAC-001/reactivar", null)).StatusCode);
        Assert.NotNull(await app.ConGateway(g => g.BuscarPacienteAsync(TelMaria, CancellationToken.None)));
    }

    [Fact]
    public async Task La_migracion_deja_activos_a_los_pacientes_que_ya_existian()
    {
        using var conexion = new SqliteConnection("Data Source=:memory:");
        conexion.Open();
        await using var db = new SqliteDpmDbContext(new DbContextOptionsBuilder<SqliteDpmDbContext>().UseSqlite(conexion).Options, new ConsultorioFijo(1));
        var migraciones = db.Database.GetMigrations().ToList();
        var anterior = migraciones[migraciones.FindIndex(m => m.EndsWith("_Contacto", StringComparison.Ordinal)) - 1];
        await db.GetService<IMigrator>().MigrateAsync(anterior);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Consultorios (Nombre, TelefonoEmergencias, IdiomaPredeterminado, EsDemo, HorizonteSeguimientoSemanas, DiasContactoSeguimiento, HoraResumenMedico, HoraLiberarCuposUrgencia, CreadoUtc) VALUES ('C (ficticio)', '911', 'es', 0, 8, 21, 7, 10, '2026-01-01')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Pacientes (ConsultorioId, Nombre, Telefono, CreadoUtc) VALUES (1, 'Ana (ficticia)', '8095550199', '2026-01-01')");

        await db.Database.MigrateAsync();
        Assert.True((await db.Pacientes.SingleAsync()).Activo);
    }
}
