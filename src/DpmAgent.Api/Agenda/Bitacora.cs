using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

public sealed record EntradaBitacora(
    long Id, DateTimeOffset Fecha, string Actor, string Accion, string? Entidad, string? EntidadId, string? SesionId, string? Detalle,
    string? Idioma = null, string? TextoOriginal = null, string? ResumenEs = null, string? ResumenEn = null);

/// <summary>Texto del paciente y resúmenes para el panel (es/en) que acompañan una entrada de la bitácora.</summary>
public sealed record TextosBitacora(string? Idioma, string? TextoOriginal, string? ResumenEs, string? ResumenEn);

/// <summary>
/// Registro de quién hizo qué y cuándo. El detalle es administrativo; el texto original del paciente
/// solo se guarda en las entradas que lo necesitan (escalamientos, respuestas bloqueadas) y se borra
/// al vencer la retención de conversaciones.
/// </summary>
public sealed class Bitacora
{
    public const string Agente = "agente";
    public const string Sistema = "sistema";

    private readonly DpmDbContext _db;
    private readonly TimeProvider _clock;

    public Bitacora(DpmDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public Task RegistrarAsync(
        string actor, string accion, string? entidad, string? entidadId, string? sesionId, string? detalle, CancellationToken ct) =>
        RegistrarAsync(actor, accion, entidad, entidadId, sesionId, detalle, null, ct);

    public async Task RegistrarAsync(
        string actor, string accion, string? entidad, string? entidadId, string? sesionId, string? detalle,
        TextosBitacora? textos, CancellationToken ct)
    {
        _db.Bitacora.Add(new RegistroBitacora
        {
            Idioma = textos?.Idioma,
            TextoOriginal = Recortar(textos?.TextoOriginal, 2000),
            ResumenEs = Recortar(textos?.ResumenEs, 1000),
            ResumenEn = Recortar(textos?.ResumenEn, 1000),
            ConsultorioId = _db.ConsultorioId,
            FechaUtc = _clock.GetUtcNow().UtcDateTime,
            Actor = Recortar(actor, 60)!,
            Accion = Recortar(accion, 60)!,
            Entidad = entidad,
            EntidadId = entidadId,
            SesionId = sesionId,
            Detalle = Recortar(detalle, 500),
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Para registrar antes de que el consultorio de la petición esté fijado (inicio de sesión).</summary>
    public async Task RegistrarEnConsultorioAsync(int consultorioId, string actor, string accion, CancellationToken ct)
    {
        _db.Bitacora.Add(new RegistroBitacora
        {
            ConsultorioId = consultorioId,
            FechaUtc = _clock.GetUtcNow().UtcDateTime,
            Actor = Recortar(actor, 60)!,
            Accion = accion,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<EntradaBitacora>> ListarAsync(int limite, CancellationToken ct)
    {
        var filas = await _db.Bitacora.AsNoTracking()
            .OrderByDescending(b => b.Id)
            .Take(Math.Clamp(limite, 1, 500))
            .ToListAsync(ct);

        return filas.Select(b => new EntradaBitacora(
            b.Id, new DateTimeOffset(b.FechaUtc, TimeSpan.Zero), b.Actor, b.Accion, b.Entidad, b.EntidadId, b.SesionId, b.Detalle,
            b.Idioma, b.TextoOriginal, b.ResumenEs, b.ResumenEn))
            .ToList();
    }

    private static string? Recortar(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
