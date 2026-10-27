using System.Security.Claims;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Api.Panel;

/// <summary>Bitácora de acceso a datos de pacientes: quién vio la ficha, quién exportó y quién borró. Solo ids y acción.</summary>
public static class RegistroAccesos
{
    public const string VerFicha = "ver_ficha";
    public const string Exportar = "exportar";
    public const string Borrar = "borrar";

    public static async Task RegistrarAsync(DpmDbContext db, ClaimsPrincipal usuario, string accion, IEnumerable<int> pacientes, TimeProvider clock, CancellationToken ct)
    {
        var ahora = clock.GetUtcNow().UtcDateTime;
        var quien = usuario.Identity?.Name ?? "?";
        foreach (var id in pacientes.Distinct())
            db.AccesosDatos.Add(new AccesoDatos { ConsultorioId = db.ConsultorioId, Usuario = quien.Length > 60 ? quien[..60] : quien, Accion = accion, PacienteId = id, FechaUtc = ahora });
        await db.SaveChangesAsync(ct);
    }
}
