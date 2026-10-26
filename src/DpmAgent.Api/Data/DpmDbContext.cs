using DpmAgent.Api.Data.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DpmAgent.Api.Data;

/// <summary>
/// Base de datos del agente. Cada proveedor tiene su subclase con sus propias migraciones
/// (<see cref="SqliteDpmDbContext"/> en local y <see cref="PostgresDpmDbContext"/> en la nube).
/// Todas las consultas se filtran por el consultorio actual.
/// </summary>
public abstract class DpmDbContext : DbContext
{
    private readonly int _consultorioId;

    protected DpmDbContext(DbContextOptions options, IConsultorioActual consultorio) : base(options)
    {
        _consultorioId = consultorio.Id;
    }

    public int ConsultorioId => _consultorioId;

    public DbSet<Consultorio> Consultorios => Set<Consultorio>();

    public DbSet<Medico> Medicos => Set<Medico>();

    public DbSet<Paciente> Pacientes => Set<Paciente>();

    public DbSet<Cita> Citas => Set<Cita>();

    public DbSet<ListaEspera> ListaEspera => Set<ListaEspera>();

    public DbSet<Escalamiento> Escalamientos => Set<Escalamiento>();

    public DbSet<Conversacion> Conversaciones => Set<Conversacion>();

    public DbSet<Mensaje> Mensajes => Set<Mensaje>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite devuelve DateTime sin Kind y PostgreSQL exige UTC: todo entra y sale como UTC.
        builder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Consultorio>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.TelefonoWhatsApp).HasMaxLength(20);
        });

        b.Entity<Medico>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.Especialidad).HasMaxLength(100);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Paciente>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.Telefono).HasMaxLength(20);
            e.Property(x => x.Ars).HasMaxLength(100);
            e.Property(x => x.NumeroAfiliado).HasMaxLength(50);
            e.HasIndex(x => new { x.ConsultorioId, x.Telefono });
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Cita>(e =>
        {
            e.Property(x => x.Motivo).HasMaxLength(200);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Medico).WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.PacienteId });

            // La base de datos es la última barrera contra la doble reserva.
            e.HasIndex(x => new { x.MedicoId, x.FechaHoraUtc })
                .IsUnique()
                .HasFilter("\"Estado\" = 'confirmada'")
                .HasDatabaseName("IX_Citas_Medico_FechaHora_Confirmada");

            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<ListaEspera>(e =>
        {
            e.ToTable("ListaEspera");
            e.Property(x => x.Estado).HasMaxLength(20);
            e.HasOne(x => x.Paciente).WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Medico).WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.MedicoId, x.Estado });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Escalamiento>(e =>
        {
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.Motivo).HasMaxLength(1000);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Conversacion>(e =>
        {
            e.ToTable("Conversaciones");
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.TelefonoCanal).HasMaxLength(20);
            e.HasIndex(x => new { x.ConsultorioId, x.SesionId }).IsUnique();
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasMany(x => x.Mensajes).WithOne().HasForeignKey(x => x.ConversacionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Mensaje>(e =>
        {
            e.Property(x => x.Rol).HasMaxLength(20);
        });
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}

public sealed class SqliteDpmDbContext(DbContextOptions<SqliteDpmDbContext> options, IConsultorioActual consultorio)
    : DpmDbContext(options, consultorio);

public sealed class PostgresDpmDbContext(DbContextOptions<PostgresDpmDbContext> options, IConsultorioActual consultorio)
    : DpmDbContext(options, consultorio);
