using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Data;

public sealed class BaseDatosOptions
{
    public const string SectionName = "BaseDatos";

    /// <summary>Sqlite (archivo local), Postgres (nube) o Memoria (SQLite en memoria: se pierde al reiniciar).</summary>
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
    /// <param name="produccion">En producción, la conexión a PostgreSQL debe exigir TLS o la app no arranca.</param>
    public static IServiceCollection AddPersistencia(this IServiceCollection services, IConfiguration config, bool produccion = false)
    {
        var opciones = config.GetSection(BaseDatosOptions.SectionName).Get<BaseDatosOptions>() ?? new BaseDatosOptions();
        services.AddSingleton(opciones);

        var proveedor = opciones.Proveedor.Trim().ToLowerInvariant();
        var conexion = config.GetConnectionString("Dpm");
        services.AddDataProtection();
        services.AddSingleton<CifradoCampos>();
        services.AddScoped(_ => new ConsultorioDelAmbito(opciones.ConsultorioId));
        services.AddScoped<IConsultorioActual>(sp => sp.GetRequiredService<ConsultorioDelAmbito>());

        switch (proveedor)
        {
            case "memoria":
                // Base compartida en memoria: vive mientras la conexión "ancla" siga abierta.
                var nombre = $"dpm-memoria-{Guid.NewGuid():N}";
                var cadena = $"Data Source={nombre};Mode=Memory;Cache=Shared";
                services.AddSingleton(new AnclaSqliteEnMemoria(cadena));
                services.AddDbContext<SqliteDpmDbContext>(o => o.UseSqlite(cadena));
                services.AddScoped<DpmDbContext>(sp => sp.GetRequiredService<SqliteDpmDbContext>());
                break;

            case "sqlite":
                services.AddDbContext<SqliteDpmDbContext>(o => o.UseSqlite(conexion ?? "Data Source=dpm-agent.db"));
                services.AddScoped<DpmDbContext>(sp => sp.GetRequiredService<SqliteDpmDbContext>());
                break;

            case "postgres":
                if (string.IsNullOrWhiteSpace(conexion))
                    throw new InvalidOperationException("Falta ConnectionStrings:Dpm para PostgreSQL (variable ConnectionStrings__Dpm).");
                if (produccion)
                    ExigirTls(conexion);
                services.AddDbContext<PostgresDpmDbContext>(o => o.UseNpgsql(conexion));
                services.AddScoped<DpmDbContext>(sp => sp.GetRequiredService<PostgresDpmDbContext>());
                break;

            default:
                throw new InvalidOperationException($"Proveedor de base de datos desconocido: {opciones.Proveedor}. Use Sqlite, Postgres o Memoria.");
        }

        if (proveedor == "memoria")
            opciones.MigrarAlIniciar = true;

        services.AddScoped<IDpmGateway, DbDpmGateway>();
        services.AddScoped<IConversationStore, DbConversationStore>();
        return services;
    }

    /// <summary>Los datos de pacientes no viajan en claro: SSL Mode debe ser Require, VerifyCA o VerifyFull.</summary>
    public static void ExigirTls(string conexion)
    {
        var modo = new Npgsql.NpgsqlConnectionStringBuilder(conexion).SslMode;
        if (modo is not (Npgsql.SslMode.Require or Npgsql.SslMode.VerifyCA or Npgsql.SslMode.VerifyFull))
            throw new InvalidOperationException(
                $"La conexión a PostgreSQL debe exigir TLS en producción (SSL Mode={modo}). Agregue \"SSL Mode=Require\" (o VerifyFull) a ConnectionStrings__Dpm.");
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

        // Llave del HMAC del teléfono y cifrado de los pacientes guardados antes de esta versión.
        await db.Cifrado.InicializarAsync(db, ct);
        var cifrados = await CifradoCampos.CifrarExistentesAsync(db, ct);
        if (cifrados > 0)
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BaseDatos").LogInformation("Se cifraron {N} pacientes existentes.", cifrados);

        if (opciones.SembrarDatosDemo)
            await DatosDemo.SembrarAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>(), ct);
    }
}

/// <summary>Mantiene abierta una conexión para que la base SQLite en memoria no desaparezca.</summary>
public sealed class AnclaSqliteEnMemoria : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _conexion;

    public AnclaSqliteEnMemoria(string cadena)
    {
        _conexion = new Microsoft.Data.Sqlite.SqliteConnection(cadena);
        _conexion.Open();
    }

    public void Dispose() => _conexion.Dispose();
}
