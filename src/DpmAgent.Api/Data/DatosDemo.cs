using DpmAgent.Api.Data.Entidades;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Data;

/// <summary>
/// Datos de demostración. Todas las personas son ficticias.
/// El consultorio sembrado queda marcado como de demostración (<see cref="Consultorio.EsDemo"/>).
/// </summary>
public static class DatosDemo
{
    public const string TelefonoMaria = "8095550101";
    public const string TelefonoJose = "8295550102";

    /// <summary>Número verificado del Dr. Luis Herrera (ficticio) en la demo: con él se prueba el canal del médico.</summary>
    public const string TelefonoMedico = "8495550111";

    /// <summary>Crea el consultorio de ejemplo con sus médicos y pacientes, si la base no tiene ningún consultorio.</summary>
    public static async Task SembrarAsync(DpmDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        if (await db.Consultorios.AnyAsync(ct))
            return;

        var consultorio = new Consultorio
        {
            Nombre = "Consultorio Ejemplo (ficticio)",
            EsDemo = true,
            CreadoUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Consultorios.Add(consultorio);
        await db.SaveChangesAsync(ct);

        await SembrarPersonasAsync(db, consultorio.Id, clock, ct);
    }

    /// <summary>Médicos y pacientes ficticios de un consultorio (también al reiniciar la demo).</summary>
    public static async Task SembrarPersonasAsync(DpmDbContext db, int consultorioId, TimeProvider clock, CancellationToken ct)
    {
        var ahora = clock.GetUtcNow().UtcDateTime;
        db.Medicos.AddRange(
            new Medico { ConsultorioId = consultorioId, Nombre = "Dra. Ana Valdez (ficticia)", Especialidad = "Medicina General" },
            new Medico { ConsultorioId = consultorioId, Nombre = "Dr. Luis Herrera (ficticio)", Especialidad = "Pediatría" });

        db.Pacientes.AddRange(
            new Paciente
            {
                ConsultorioId = consultorioId,
                Nombre = "María Rodríguez (ficticia)",
                Telefono = TelefonoMaria,
                Ars = "ARS Ejemplo",
                CreadoUtc = ahora,
            },
            new Paciente
            {
                ConsultorioId = consultorioId,
                Nombre = "José Martínez (ficticio)",
                Telefono = TelefonoJose,
                CreadoUtc = ahora,
            });

        await db.SaveChangesAsync(ct);

        // María y José dieron su permiso para avisos en la recepción (ficticio): así la demo muestra
        // el recordatorio y la oferta de la lista de espera. Otros pacientes nuevos empiezan sin permiso.
        var ids = await db.Pacientes.IgnoreQueryFilters()
            .Where(p => p.ConsultorioId == consultorioId && (p.Telefono == TelefonoMaria || p.Telefono == TelefonoJose))
            .Select(p => new { p.Id, p.Telefono })
            .ToListAsync(ct);
        foreach (var p in ids)
        {
            foreach (var categoria in Cumplimiento.CategoriasConsentimiento.Todas)
            {
                db.Consentimientos.Add(new Consentimiento
                {
                    ConsultorioId = consultorioId,
                    PacienteId = p.Id,
                    Telefono = p.Telefono,
                    Categoria = categoria,
                    Estado = "otorgado",
                    Canal = Cumplimiento.CanalesConsentimiento.Presencial,
                    VersionTexto = Cumplimiento.ServicioConsentimiento.VersionTexto,
                    Idioma = "es",
                    RegistradoPor = "semilla",
                    FechaUtc = ahora,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
