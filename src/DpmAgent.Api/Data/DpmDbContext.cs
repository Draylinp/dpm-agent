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

    public DbSet<UsuarioPanel> UsuariosPanel => Set<UsuarioPanel>();

    public DbSet<Medico> Medicos => Set<Medico>();

    public DbSet<Paciente> Pacientes => Set<Paciente>();

    public DbSet<Cita> Citas => Set<Cita>();

    public DbSet<ListaEspera> ListaEspera => Set<ListaEspera>();

    public DbSet<Escalamiento> Escalamientos => Set<Escalamiento>();

    public DbSet<OfertaHueco> OfertasHueco => Set<OfertaHueco>();

    public DbSet<MensajeSaliente> MensajesSalientes => Set<MensajeSaliente>();

    public DbSet<MensajeEntrante> MensajesEntrantes => Set<MensajeEntrante>();

    public DbSet<RegistroBitacora> Bitacora => Set<RegistroBitacora>();

    public DbSet<AutorizacionArs> AutorizacionesArs => Set<AutorizacionArs>();

    public DbSet<CarnetPendiente> CarnetsPendientes => Set<CarnetPendiente>();

    public DbSet<Conversacion> Conversaciones => Set<Conversacion>();

    public DbSet<Mensaje> Mensajes => Set<Mensaje>();

    public DbSet<RegistroUsoModelo> UsosModelo => Set<RegistroUsoModelo>();

    public DbSet<Consentimiento> Consentimientos => Set<Consentimiento>();

    public DbSet<Seguimiento> Seguimientos => Set<Seguimiento>();

    public DbSet<Feriado> Feriados => Set<Feriado>();

    public DbSet<BloqueoAgenda> BloqueosAgenda => Set<BloqueoAgenda>();

    public DbSet<SolicitudSobrecupo> SolicitudesSobrecupo => Set<SolicitudSobrecupo>();

    public DbSet<CupoUrgencia> CuposUrgencia => Set<CupoUrgencia>();

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
            e.Property(x => x.WhatsAppPhoneNumberId).HasMaxLength(40);
            e.Property(x => x.TelefonoEmergencias).HasMaxLength(20).HasDefaultValue("911");
            e.Property(x => x.IdiomaPredeterminado).HasMaxLength(10).HasDefaultValue("es");
            e.Property(x => x.NombreAsistente).HasMaxLength(60);
            e.Property(x => x.CentrosEmergencia).HasMaxLength(1000);
            e.HasIndex(x => x.WhatsAppPhoneNumberId).IsUnique();
        });

        // Sin filtro por consultorio: el inicio de sesión busca el usuario antes de saber a qué consultorio pertenece.
        b.Entity<UsuarioPanel>(e =>
        {
            e.ToTable("UsuariosPanel");
            e.Property(x => x.Usuario).HasMaxLength(60);
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.HashClave).HasMaxLength(200);
            e.Property(x => x.Rol).HasMaxLength(20);
            e.HasIndex(x => x.Usuario).IsUnique();
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
        });

        b.Entity<Medico>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.Especialidad).HasMaxLength(100);
            e.Property(x => x.Telefono).HasMaxLength(20);
            e.Property(x => x.CodigoVerificacionHash).HasMaxLength(100);
            e.HasIndex(x => x.Telefono);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Paciente>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.Telefono).HasMaxLength(20);
            e.Property(x => x.Ars).HasMaxLength(100);
            e.Property(x => x.NumeroAfiliado).HasMaxLength(50);
            e.Property(x => x.Idioma).HasMaxLength(10);
            e.HasIndex(x => new { x.ConsultorioId, x.Telefono });
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Cita>(e =>
        {
            e.Property(x => x.Motivo).HasMaxLength(200);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Reprogramacion).HasMaxLength(20);
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
            e.Property(x => x.AtendidoPor).HasMaxLength(60);
            e.Property(x => x.MotivoEn).HasMaxLength(1000);
            e.Property(x => x.MensajeOriginal).HasMaxLength(2000);
            e.Property(x => x.Idioma).HasMaxLength(10);
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<OfertaHueco>(e =>
        {
            e.ToTable("OfertasHueco");
            e.Property(x => x.Estado).HasMaxLength(20).IsConcurrencyToken();
            e.HasOne<ListaEspera>().WithMany().HasForeignKey(x => x.ListaEsperaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Medico>().WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            e.HasIndex(x => new { x.MedicoId, x.FechaHoraUtc });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<MensajeSaliente>(e =>
        {
            e.ToTable("MensajesSalientes");
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.Canal).HasMaxLength(20);
            e.Property(x => x.Destino).HasMaxLength(20);
            e.Property(x => x.Texto).HasMaxLength(2000);
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.Plantilla).HasMaxLength(40);
            e.Property(x => x.Idioma).HasMaxLength(10);
            e.Property(x => x.ParametrosJson).HasMaxLength(2000);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => x.Estado);
            e.HasIndex(x => new { x.ConsultorioId, x.SesionId });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<MensajeEntrante>(e =>
        {
            e.ToTable("MensajesEntrantes");
            e.Property(x => x.ProveedorId).HasMaxLength(200);
            e.Property(x => x.Telefono).HasMaxLength(20);
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.Texto).HasMaxLength(4096);
            e.Property(x => x.MediaId).HasMaxLength(200);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => x.ProveedorId).IsUnique();
            e.HasIndex(x => x.Estado);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<RegistroBitacora>(e =>
        {
            e.ToTable("Bitacora");
            e.Property(x => x.Actor).HasMaxLength(60);
            e.Property(x => x.Accion).HasMaxLength(60);
            e.Property(x => x.Entidad).HasMaxLength(30);
            e.Property(x => x.EntidadId).HasMaxLength(30);
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.Detalle).HasMaxLength(500);
            e.Property(x => x.Idioma).HasMaxLength(10);
            e.Property(x => x.TextoOriginal).HasMaxLength(2000);
            e.Property(x => x.ResumenEs).HasMaxLength(1000);
            e.Property(x => x.ResumenEn).HasMaxLength(1000);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.FechaUtc });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<AutorizacionArs>(e =>
        {
            e.ToTable("AutorizacionesArs");
            e.Property(x => x.Contenido).HasMaxLength(4000);
            e.Property(x => x.Estado).HasMaxLength(20).IsConcurrencyToken();
            e.Property(x => x.CreadaPor).HasMaxLength(60);
            e.Property(x => x.AprobadaPor).HasMaxLength(60);
            e.HasOne<Cita>().WithMany().HasForeignKey(x => x.CitaId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            // Un solo borrador vivo por cita.
            e.HasIndex(x => x.CitaId).IsUnique().HasFilter("\"Estado\" <> 'descartada'");
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<CarnetPendiente>(e =>
        {
            e.ToTable("CarnetsPendientes");
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.TelefonoCanal).HasMaxLength(20);
            e.Property(x => x.Nombre).HasMaxLength(Dpm.DatosCarnet.MaxNombre);
            e.Property(x => x.Ars).HasMaxLength(Dpm.DatosCarnet.MaxArs);
            e.Property(x => x.NumeroAfiliado).HasMaxLength(Dpm.DatosCarnet.MaxAfiliado);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.ResueltoPor).HasMaxLength(20);
            e.Property(x => x.Estado).IsConcurrencyToken();
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            e.HasIndex(x => new { x.ConsultorioId, x.SesionId });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Conversacion>(e =>
        {
            e.ToTable("Conversaciones");
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.TelefonoCanal).HasMaxLength(20);
            e.Property(x => x.CancelacionPropuesta).HasMaxLength(30);
            e.Property(x => x.ConsentimientoVersion).HasMaxLength(20);
            e.Property(x => x.PropuestaMedico).HasMaxLength(1000);
            e.Property(x => x.Idioma).HasMaxLength(10);
            e.HasIndex(x => new { x.ConsultorioId, x.SesionId }).IsUnique();
            e.HasIndex(x => x.ActualizadaUtc);
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasMany(x => x.Mensajes).WithOne().HasForeignKey(x => x.ConversacionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Mensaje>(e =>
        {
            e.Property(x => x.Rol).HasMaxLength(20);
        });

        b.Entity<Seguimiento>(e =>
        {
            e.ToTable("Seguimientos");
            e.Property(x => x.Motivo).HasMaxLength(60);
            e.Property(x => x.RecurrenciaUnidad).HasMaxLength(10);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.CreadoPor).HasMaxLength(60);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Medico>().WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<SolicitudSobrecupo>(e =>
        {
            e.ToTable("SolicitudesSobrecupo");
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.DecididaPor).HasMaxLength(60);
            e.HasIndex(x => new { x.ConsultorioId, x.Estado });
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Medico>().WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<CupoUrgencia>(e =>
        {
            e.ToTable("CuposUrgencia");
            e.HasIndex(x => new { x.ConsultorioId, x.MedicoId, x.DiaSemana, x.Hora }).IsUnique();
            e.HasOne<Medico>().WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Feriado>(e =>
        {
            e.ToTable("Feriados");
            e.Property(x => x.Nombre).HasMaxLength(100);
            e.HasIndex(x => new { x.ConsultorioId, x.Fecha }).IsUnique();
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<BloqueoAgenda>(e =>
        {
            e.ToTable("BloqueosAgenda");
            e.Property(x => x.Motivo).HasMaxLength(60);
            e.Property(x => x.CreadoPor).HasMaxLength(60);
            e.HasIndex(x => new { x.ConsultorioId, x.MedicoId, x.Desde });
            e.HasOne<Medico>().WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<Consentimiento>(e =>
        {
            e.ToTable("Consentimientos");
            e.Property(x => x.Telefono).HasMaxLength(20);
            e.Property(x => x.Categoria).HasMaxLength(20);
            e.Property(x => x.Estado).HasMaxLength(10);
            e.Property(x => x.Canal).HasMaxLength(20);
            e.Property(x => x.VersionTexto).HasMaxLength(20);
            e.Property(x => x.Idioma).HasMaxLength(10);
            e.Property(x => x.RegistradoPor).HasMaxLength(60);
            e.HasIndex(x => new { x.ConsultorioId, x.PacienteId, x.Categoria });
            e.HasOne<Paciente>().WithMany().HasForeignKey(x => x.PacienteId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Medico>().WithMany().HasForeignKey(x => x.MedicoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
        });

        b.Entity<RegistroUsoModelo>(e =>
        {
            e.ToTable("UsosModelo");
            e.Property(x => x.Proveedor).HasMaxLength(40);
            e.Property(x => x.Modelo).HasMaxLength(120);
            e.Property(x => x.Proposito).HasMaxLength(20);
            e.Property(x => x.SesionId).HasMaxLength(100);
            e.HasOne<Consultorio>().WithMany().HasForeignKey(x => x.ConsultorioId);
            e.HasIndex(x => new { x.ConsultorioId, x.FechaUtc });
            e.HasQueryFilter(x => x.ConsultorioId == _consultorioId);
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
