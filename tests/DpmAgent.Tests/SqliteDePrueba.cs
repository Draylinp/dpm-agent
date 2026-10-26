using DpmAgent.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Tests;

/// <summary>
/// Base SQLite real (en memoria o en archivo) con las migraciones aplicadas y los datos de demo.
/// Cada contexto creado comparte la misma base, como varias peticiones de la app.
/// </summary>
public sealed class SqliteDePrueba : IDisposable
{
    private readonly SqliteConnection _conexion;
    private readonly List<DpmDbContext> _contextos = [];

    public SqliteDePrueba(TimeProvider clock, string cadena = "Data Source=:memory:")
    {
        _conexion = new SqliteConnection(cadena);
        _conexion.Open();

        using var db = NuevoContexto(1, registrar: false);
        db.Database.Migrate();
        DatosDemo.SembrarAsync(db, clock).GetAwaiter().GetResult();
    }

    public DpmDbContext Contexto(int consultorioId = 1) => NuevoContexto(consultorioId, registrar: true);

    private SqliteDpmDbContext NuevoContexto(int consultorioId, bool registrar)
    {
        var db = new SqliteDpmDbContext(
            new DbContextOptionsBuilder<SqliteDpmDbContext>().UseSqlite(_conexion).Options,
            new ConsultorioFijo(consultorioId));
        if (registrar)
            _contextos.Add(db);
        return db;
    }

    public void Dispose()
    {
        foreach (var db in _contextos)
            db.Dispose();
        _conexion.Dispose();
    }
}
