using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DpmAgent.Api.Data;

// Solo las usa `dotnet ef` para generar migraciones; no abren ninguna conexión real.

public sealed class SqliteDpmDbContextFactory : IDesignTimeDbContextFactory<SqliteDpmDbContext>
{
    public SqliteDpmDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqliteDpmDbContext>().UseSqlite("Data Source=diseno.db").Options,
        new ConsultorioFijo(0));
}

public sealed class PostgresDpmDbContextFactory : IDesignTimeDbContextFactory<PostgresDpmDbContext>
{
    public PostgresDpmDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PostgresDpmDbContext>().UseNpgsql("Host=localhost;Database=dpm_diseno").Options,
        new ConsultorioFijo(0));
}
