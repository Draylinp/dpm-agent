using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Data;

public sealed class BaseDatosOptions
{
    public const string SectionName = "BaseDatos";

    /// <summary>Sqlite (local), Postgres (nube) o Memoria (sin persistencia, datos simulados).</summary>
    public string Proveedor { get; set; } = "Sqlite";

    public bool MigrarAlIniciar { get; set; } = true;

    /// <summary>Crea el consultorio y los datos ficticios de demo si la base está vacía.</summary>
    public bool SembrarDatosDemo { get; set; } = true;

    /// <summary>Consultorio que atiende esta instancia hasta que los canales lo resuelvan por número (F5).</summary>
    public int ConsultorioId { get; set; } = 1;
}

public static class PersistenciaExtensions
{
    /// <summary>
    /// Registra el gateway del consultorio y el historial de conversaciones según <c>BaseDatos:Proveedor</c>.
    /// La cadena de conexión se lee de <c>ConnectionStrings:Dpm</c> (en la nube, por variable de entorno).
    /// </summary>
    public static IServiceCollection AddPersistencia(this IServiceCollection services, IConfiguration config)
    {
        var opciones = config.GetSection(BaseDatosOptions.SectionName).Get<BaseDatosOptions>() ?? new BaseDatosOptions();
        services.AddSingleton(opciones);

        var proveedor = opciones.Proveedor.Trim().ToLowerInvariant();
        if (proveedor == "memoria")
        {
            services.AddSingleton<IDpmGateway, MockDpmGateway>();
            services.AddSingleton<IConversationStore, InMemoryConversationStore>();
            return services;
        }

        var conexion = config.GetConnectionString("Dpm");
        services.AddSingleton<IConsultorioActual>(new ConsultorioFijo(opciones.ConsultorioId));

        switch (proveedor)
        {
            case "sqlite":
                services.AddDbContext<SqliteDpmDbContext>(o => o.UseSqlite(conexion ?? "Data Source=dpm-agent.db"));
                services.AddScoped<DpmDbContext>(sp => sp.GetRequiredService<SqliteDpmDbContext>());
                break;

            case "postgres":
                if (string.IsNullOrWhiteSpace(conexion))
                    throw new InvalidOperationException("Falta ConnectionStrings:Dpm para PostgreSQL (variable ConnectionStrings__Dpm).");
                services.AddDbContext<PostgresDpmDbContext>(o => o.UseNpgsql(conexion));
                services.AddScoped<DpmDbContext>(sp => sp.GetRequiredService<PostgresDpmDbContext>());
                break;

            default:
                throw new InvalidOperationException($"Proveedor de base de datos desconocido: {opciones.Proveedor}. Use Sqlite, Postgres o Memoria.");
        }

        services.AddScoped<IDpmGateway, DbDpmGateway>();
        services.AddScoped<IConversationStore, DbConversationStore>();
        return services;
    }

    /// <summary>Aplica las migraciones pendientes y siembra los datos de demo, según la configuración.</summary>
    public static async Task InicializarBaseDatosAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var opciones = services.GetRequiredService<BaseDatosOptions>();
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetService<DpmDbContext>();
        if (db is null)
            return;

        if (opciones.MigrarAlIniciar)
            await db.Database.MigrateAsync(ct);

        if (opciones.SembrarDatosDemo)
            await DatosDemo.SembrarAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>(), ct);
    }
}
