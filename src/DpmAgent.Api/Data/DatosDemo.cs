using DpmAgent.Api.Data.Entidades;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Data;

/// <summary>
/// Datos de demostración. Todas las personas son ficticias.
/// Solo se crean si la base no tiene ningún consultorio.
/// </summary>
public static class DatosDemo
{
    public static async Task SembrarAsync(DpmDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        if (await db.Consultorios.AnyAsync(ct))
            return;

        var ahora = clock.GetUtcNow().UtcDateTime;
        var consultorio = new Consultorio { Nombre = "Consultorio Ejemplo (ficticio)", CreadoUtc = ahora };
        db.Consultorios.Add(consultorio);
        await db.SaveChangesAsync(ct);

        db.Medicos.AddRange(
            new Medico { ConsultorioId = consultorio.Id, Nombre = "Dra. Ana Valdez (ficticia)", Especialidad = "Medicina General" },
            new Medico { ConsultorioId = consultorio.Id, Nombre = "Dr. Luis Herrera (ficticio)", Especialidad = "Pediatría" });

        db.Pacientes.AddRange(
            new Paciente
            {
                ConsultorioId = consultorio.Id,
                Nombre = "María Rodríguez (ficticia)",
                Telefono = Dpm.Telefono.Normalizar("809-555-0101")!,
                Ars = "ARS Ejemplo",
                CreadoUtc = ahora,
            },
            new Paciente
            {
                ConsultorioId = consultorio.Id,
                Nombre = "José Martínez (ficticio)",
                Telefono = Dpm.Telefono.Normalizar("829-555-0102")!,
                CreadoUtc = ahora,
            });

        await db.SaveChangesAsync(ct);
    }
}
