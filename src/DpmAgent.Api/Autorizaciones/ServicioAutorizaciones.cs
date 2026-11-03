using System.Globalization;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Autorizaciones;

public sealed record BorradorAutorizacion(
    string Id,
    string CitaId,
    string PacienteId,
    string Paciente,
    string? Ars,
    string Estado,
    string Contenido,
    IReadOnlyList<string> Faltantes,
    DateTimeOffset Creada,
    string CreadaPor,
    string? AprobadaPor);

/// <summary>
/// Borradores de solicitud de autorización a la ARS. Solo datos administrativos de la cita
/// (nada clínico) y con una plantilla fija. La secretaria revisa, edita y aprueba;
/// aprobar no envía nada.
/// </summary>
public sealed class ServicioAutorizaciones
{
    public const string MarcaFalta = "[FALTA:";
    public const int MaxContenido = 4000;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-DO");

    private readonly DpmDbContext _db;
    private readonly TimeProvider _clock;

    public ServicioAutorizaciones(DpmDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>Crea el borrador de una cita confirmada, o devuelve el que ya existe.</summary>
    public async Task<BorradorAutorizacion> CrearAsync(string citaId, string usuario, CancellationToken ct)
    {
        var id = IdsDpm.Leer(citaId, "CITA");
        var cita = id is null ? null : await _db.Citas
            .Include(c => c.Paciente)
            .Include(c => c.Medico)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cita is null)
            throw new ArgumentException($"No existe la cita {citaId}.");
        if (cita.Estado != "confirmada")
            throw new InvalidOperationException("Solo se prepara autorización para citas confirmadas.");

        var existente = await _db.AutorizacionesArs.FirstOrDefaultAsync(a => a.CitaId == cita.Id && a.Estado != "descartada", ct);
        if (existente is not null)
            return await ABorradorAsync(existente, ct);

        var consultorio = await _db.Consultorios.AsNoTracking().FirstAsync(c => c.Id == _db.ConsultorioId, ct);
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var a = new AutorizacionArs
        {
            ConsultorioId = _db.ConsultorioId,
            CitaId = cita.Id,
            PacienteId = cita.PacienteId,
            Contenido = Plantilla(cita, consultorio.Nombre, ahora),
            Estado = "borrador",
            CreadaUtc = ahora,
            CreadaPor = usuario,
            ActualizadaUtc = ahora,
        };
        _db.AutorizacionesArs.Add(a);
        await _db.SaveChangesAsync(ct);
        return await ABorradorAsync(a, ct);
    }

    public async Task<IReadOnlyList<BorradorAutorizacion>> PendientesAsync(CancellationToken ct)
    {
        var lista = await _db.AutorizacionesArs.Where(a => a.Estado == "borrador").OrderBy(a => a.Id).ToListAsync(ct);
        var resultado = new List<BorradorAutorizacion>();
        foreach (var a in lista)
            resultado.Add(await ABorradorAsync(a, ct));
        return resultado;
    }

    public async Task<BorradorAutorizacion> EditarAsync(string autorizacionId, string contenido, CancellationToken ct)
    {
        var a = await BorradorAsync(autorizacionId, ct);
        a.Contenido = Validar(contenido);
        a.ActualizadaUtc = _clock.GetUtcNow().UtcDateTime;
        await GuardarAsync(ct);
        return await ABorradorAsync(a, ct);
    }

    /// <summary>Marca el borrador como revisado y listo. No lo envía a ninguna parte.</summary>
    public async Task<BorradorAutorizacion> AprobarAsync(string autorizacionId, string? contenido, string usuario, CancellationToken ct)
    {
        var a = await BorradorAsync(autorizacionId, ct);
        if (contenido is not null)
            a.Contenido = Validar(contenido);

        if (a.Contenido.Contains(MarcaFalta, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Complete los datos marcados con [FALTA: ...] antes de aprobar.");

        var ahora = _clock.GetUtcNow().UtcDateTime;
        a.Estado = "aprobada";
        a.AprobadaUtc = ahora;
        a.AprobadaPor = usuario;
        a.ActualizadaUtc = ahora;
        await GuardarAsync(ct);
        return await ABorradorAsync(a, ct);
    }

    public async Task<BorradorAutorizacion> DescartarAsync(string autorizacionId, CancellationToken ct)
    {
        var a = await BorradorAsync(autorizacionId, ct);
        a.Estado = "descartada";
        a.ActualizadaUtc = _clock.GetUtcNow().UtcDateTime;
        await GuardarAsync(ct);
        return await ABorradorAsync(a, ct);
    }

    internal static string Plantilla(Data.Entidades.Cita cita, string consultorio, DateTime ahoraUtc)
    {
        var p = cita.Paciente!;
        var m = cita.Medico!;
        var fecha = TimeZoneInfo.ConvertTime(new DateTimeOffset(cita.FechaHoraUtc, TimeSpan.Zero), HorarioConsultorio.ZonaRd);
        var hoy = TimeZoneInfo.ConvertTime(new DateTimeOffset(ahoraUtc, TimeSpan.Zero), HorarioConsultorio.ZonaRd);

        return string.Join('\n',
            "SOLICITUD DE AUTORIZACIÓN DE SERVICIO (BORRADOR, NO ENVIADO)",
            "",
            $"ARS: {p.Ars ?? Falta("ARS")}",
            $"Afiliado: {p.Nombre}",
            $"Número de afiliado / NSS: {p.NumeroAfiliado ?? Falta("número de afiliado")}",
            $"Teléfono de contacto: {p.Telefono}",
            "",
            $"Prestador: {consultorio}",
            $"Médico: {m.Nombre}",
            $"Especialidad: {m.Especialidad}",
            $"Fecha y hora del servicio: {fecha.ToString("dddd d 'de' MMMM 'de' yyyy, h:mm tt", Es)}",
            $"Tipo de servicio: consulta ({cita.Motivo})",
            $"Referencia interna: {IdsDpm.Cita(cita.Id)}",
            "",
            "Observaciones administrativas: ",
            "",
            $"Preparado automáticamente el {hoy.ToString("d 'de' MMMM 'de' yyyy", Es)} con datos administrativos de la cita.",
            "Revisado y aprobado por: ");
    }

    private static string Falta(string dato) => $"{MarcaFalta} {dato}]";

    private static string Validar(string contenido)
    {
        if (string.IsNullOrWhiteSpace(contenido))
            throw new ArgumentException("El contenido no puede quedar vacío.");
        if (contenido.Length > MaxContenido)
            throw new ArgumentException($"El contenido no puede pasar de {MaxContenido} caracteres.");
        return contenido.Replace("\r\n", "\n");
    }

    private async Task<AutorizacionArs> BorradorAsync(string autorizacionId, CancellationToken ct)
    {
        var id = IdsDpm.Leer(autorizacionId, "AUT");
        var a = id is null ? null : await _db.AutorizacionesArs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null)
            throw new ArgumentException($"No existe la autorización {autorizacionId}.");
        if (a.Estado != "borrador")
            throw new InvalidOperationException($"La autorización ya fue {a.Estado}.");
        return a;
    }

    private async Task GuardarAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("Otra persona cambió esta autorización. Recargue e intente de nuevo.");
        }
    }

    private async Task<BorradorAutorizacion> ABorradorAsync(AutorizacionArs a, CancellationToken ct)
    {
        var p = await _db.Pacientes.AsNoTracking().FirstAsync(x => x.Id == a.PacienteId, ct);
        var faltantes = new List<string>();
        if (p.Ars is null) faltantes.Add("ARS");
        if (p.NumeroAfiliado is null) faltantes.Add("número de afiliado");

        return new BorradorAutorizacion(
            $"AUT-{a.Id}", IdsDpm.Cita(a.CitaId), IdsDpm.Paciente(a.PacienteId), p.Nombre, p.Ars, a.Estado, a.Contenido,
            a.Estado == "borrador" && a.Contenido.Contains(MarcaFalta, StringComparison.OrdinalIgnoreCase) ? faltantes : [],
            new DateTimeOffset(a.CreadaUtc, TimeSpan.Zero), a.CreadaPor, a.AprobadaPor);
    }
}
