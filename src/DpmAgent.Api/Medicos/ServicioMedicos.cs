using System.Security.Cryptography;
using System.Text;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Idiomas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Medicos;

/// <summary>Opciones del canal del médico (sección "CanalMedico").</summary>
public sealed class CanalMedicoOptions
{
    public const string SectionName = "CanalMedico";

    /// <summary>
    /// El chat web no garantiza el número (en la demo es simulado). Por defecto el médico solo se reconoce
    /// por WhatsApp, cuyo remitente lo garantiza Meta. Activarlo solo en la demo.
    /// </summary>
    public bool PermitirChatWeb { get; set; }

    public int MinutosCodigo { get; set; } = 30;
}

/// <summary>Médico reconocido en el canal: solo id y nombre.</summary>
public sealed record MedicoCanal(int Id, string Nombre);

public sealed record EstadoMedico(string Id, string Nombre, string? Telefono, bool Verificado, bool Resumen);

/// <summary>
/// Teléfono verificado de cada médico, su permiso para el resumen diario y el resumen mismo.
/// Un número que no está registrado y verificado como médico nunca obtiene herramientas de médico.
/// </summary>
public sealed class ServicioMedicos(
    DpmDbContext db, Bitacora bitacora, Notificador notificador, TimeProvider clock, IOptions<CanalMedicoOptions> opciones,
    IOptions<Demo.DemoOptions> demo)
{
    private readonly CanalMedicoOptions _opciones = opciones.Value;

    public bool PermiteChatWeb => _opciones.PermitirChatWeb;

    public async Task<MedicoCanal?> MedicoDelCanalAsync(string? telefono, string canal, CancellationToken ct)
    {
        if (telefono is null)
            return null;
        // El chat web no garantiza el número: solo se acepta si se permite expresamente o en la demo pública (datos ficticios).
        if (canal != CanalesConocidos.WhatsApp && !_opciones.PermitirChatWeb &&
            !(demo.Value.Habilitado && await db.Consultorios.AnyAsync(c => c.Id == db.ConsultorioId && c.EsDemo, ct)))
            return null;
        var normalizado = Telefono.Normalizar(telefono);
        return await db.Medicos.AsNoTracking()
            .Where(m => m.Activo && m.Telefono == normalizado && m.TelefonoVerificadoUtc != null)
            .Select(m => new MedicoCanal(m.Id, m.Nombre))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Guarda el teléfono del médico y devuelve un código de un solo uso para verificarlo desde ese teléfono.</summary>
    public async Task<string> RegistrarTelefonoAsync(string medicoId, string telefono, string actor, CancellationToken ct)
    {
        var id = IdsDpm.Leer(medicoId, "MED") ?? throw new ArgumentException($"No existe el médico {medicoId}.");
        var medico = await db.Medicos.FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw new ArgumentException($"No existe el médico {medicoId}.");
        var normalizado = Telefono.Normalizar(telefono) ?? throw new ArgumentException("El teléfono no es válido.");
        if (await db.Pacientes.AnyAsync(p => p.Telefono == normalizado, ct) || await db.Medicos.AnyAsync(m => m.Id != id && m.Telefono == normalizado, ct))
            throw new ArgumentException("Ese teléfono ya pertenece a un paciente u otro médico.");

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
        medico.Telefono = normalizado;
        medico.TelefonoVerificadoUtc = null;
        medico.CodigoVerificacionHash = Hash(id, codigo);
        medico.CodigoVenceUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(_opciones.MinutosCodigo);
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, "medico_telefono", "medico", IdsDpm.Medico(id), null, "pendiente de verificar", ct);
        return codigo;
    }

    /// <summary>El médico envió "VERIFICAR 123456" desde su teléfono: si el código coincide y no venció, queda verificado.</summary>
    public async Task<MedicoCanal?> VerificarAsync(string? telefono, string codigo, string sesionId, CancellationToken ct)
    {
        var normalizado = Telefono.Normalizar(telefono);
        if (normalizado is null)
            return null;
        var ahora = clock.GetUtcNow().UtcDateTime;
        var medico = await db.Medicos.FirstOrDefaultAsync(m => m.Activo && m.Telefono == normalizado && m.CodigoVerificacionHash != null, ct);
        if (medico is null || medico.CodigoVenceUtc < ahora || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(medico.CodigoVerificacionHash!), Encoding.UTF8.GetBytes(Hash(medico.Id, codigo))))
            return null;

        medico.TelefonoVerificadoUtc = ahora;
        medico.CodigoVerificacionHash = null;
        medico.CodigoVenceUtc = null;
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(Bitacora.Sistema, "medico_verificado", "medico", IdsDpm.Medico(medico.Id), sesionId, null, ct);
        return new MedicoCanal(medico.Id, medico.Nombre);
    }

    public async Task<IReadOnlyList<EstadoMedico>> ListarAsync(CancellationToken ct)
    {
        var medicos = await db.Medicos.AsNoTracking().OrderBy(m => m.Id).ToListAsync(ct);
        var lista = new List<EstadoMedico>();
        foreach (var m in medicos)
            lista.Add(new EstadoMedico(IdsDpm.Medico(m.Id), m.Nombre, m.Telefono, m.TelefonoVerificadoUtc != null, await ResumenVigenteAsync(m.Id, ct)));
        return lista;
    }

    public Task<bool> ResumenVigenteAsync(int medicoId, CancellationToken ct) =>
        db.Consentimientos.AsNoTracking()
            .Where(c => c.MedicoId == medicoId && c.Categoria == CategoriasConsentimiento.ResumenMedico)
            .OrderByDescending(c => c.FechaUtc).ThenByDescending(c => c.Id)
            .Select(c => c.Estado == "otorgado").FirstOrDefaultAsync(ct);

    /// <summary>El médico da o retira su permiso para el resumen diario (por el canal o en persona).</summary>
    public async Task RegistrarResumenAsync(int medicoId, bool otorgado, string canal, string actor, CancellationToken ct)
    {
        var m = await db.Medicos.AsNoTracking().FirstAsync(x => x.Id == medicoId, ct);
        db.Consentimientos.Add(new Consentimiento
        {
            ConsultorioId = db.ConsultorioId,
            MedicoId = medicoId,
            Telefono = m.Telefono ?? "",
            Categoria = CategoriasConsentimiento.ResumenMedico,
            Estado = otorgado ? "otorgado" : "revocado",
            Canal = canal,
            VersionTexto = ServicioConsentimiento.VersionTexto,
            RegistradoPor = actor.Length > 60 ? actor[..60] : actor,
            FechaUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, otorgado ? "consentimiento_otorgado" : "consentimiento_revocado", "medico", IdsDpm.Medico(medicoId), null,
            $"{CategoriasConsentimiento.ResumenMedico} por {canal}", ct);
    }

    /// <summary>
    /// Resumen administrativo del día: citas (hora, nombre, motivo administrativo), sobrecupos por decidir y pacientes de
    /// seguimiento sin respuesta. Nunca datos de salud.
    /// </summary>
    public async Task<MensajeAgenda> ResumenAsync(int medicoId, DateOnly fecha, string idioma, CancellationToken ct)
    {
        var i = Idiomas.Idiomas.ParaTextos(idioma);
        var medico = await db.Medicos.AsNoTracking().FirstAsync(m => m.Id == medicoId, ct);
        var consultorio = await db.Consultorios.AsNoTracking().Where(c => c.Id == db.ConsultorioId).Select(c => c.Nombre).FirstAsync(ct);
        var desde = HorarioConsultorio.EnHoraRd(fecha, TimeOnly.MinValue).UtcDateTime;
        var hasta = HorarioConsultorio.EnHoraRd(fecha.AddDays(1), TimeOnly.MinValue).UtcDateTime;
        var citas = await db.Citas.AsNoTracking().Include(c => c.Paciente)
            .Where(c => c.MedicoId == medicoId && c.Estado == "confirmada" && c.FechaHoraUtc >= desde && c.FechaHoraUtc < hasta)
            .OrderBy(c => c.FechaHoraUtc).ToListAsync(ct);
        var sobrecupos = await SobrecuposPendientesAsync(medicoId, ct);
        var sinRespuesta = await db.Seguimientos.CountAsync(s => s.MedicoId == medicoId && s.Estado == ServicioSeguimientos.SinRespuesta, ct);

        string[] p = [consultorio, medico.Nombre, citas.Count.ToString(), sobrecupos.ToString(), sinRespuesta.ToString()];
        var cabecera = Textos.Obtener("agenda.resumen_medico", i, p);
        var detalle = citas.Count == 0
            ? Textos.Obtener("medico.sin_citas", i)
            : string.Join("\n", citas.Select(c => $"{Textos.Hora(new DateTimeOffset(c.FechaHoraUtc, TimeSpan.Zero), i)} · {c.Paciente!.Nombre} · {c.Motivo}"));
        return new MensajeAgenda($"{cabecera}\n\n{detalle}", "resumen_medico", p, i);
    }

    internal Task<int> SobrecuposPendientesAsync(int medicoId, CancellationToken ct) =>
        db.SolicitudesSobrecupo.CountAsync(s => s.MedicoId == medicoId && s.Estado == "pendiente", ct);

    /// <summary>Un resumen por médico y día, a la hora del consultorio, solo con su permiso y en días de consulta.</summary>
    public async Task<int> ProcesarResumenesAsync(CancellationToken ct)
    {
        var ahora = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), HorarioConsultorio.ZonaRd);
        var hoy = DateOnly.FromDateTime(ahora.DateTime);
        var hora = await db.Consultorios.AsNoTracking().Where(c => c.Id == db.ConsultorioId).Select(c => c.HoraResumenMedico).FirstAsync(ct);
        if (ahora.Hour < hora || HorarioConsultorio.TurnosDelDia(hoy).Count == 0 || await db.Feriados.AnyAsync(f => f.Fecha == hoy, ct))
            return 0;

        var enviados = 0;
        var medicos = await db.Medicos.Where(m => m.Activo && m.TelefonoVerificadoUtc != null && m.Telefono != null && m.UltimoResumen != hoy).ToListAsync(ct);
        foreach (var m in medicos)
        {
            m.UltimoResumen = hoy;
            await db.SaveChangesAsync(ct);
            if (!await ResumenVigenteAsync(m.Id, ct))
                continue;
            var idioma = await db.Conversaciones.AsNoTracking().Where(c => c.SesionId == CanalesConocidos.SesionWhatsApp(m.Telefono!))
                .Select(c => c.Idioma).FirstOrDefaultAsync(ct) ?? Idiomas.Idiomas.Espanol;
            await notificador.NotificarMedicoAsync(m.Telefono!, await ResumenAsync(m.Id, hoy, idioma, ct), "resumen_medico", ct);
            enviados++;
        }

        return enviados;
    }

    private static string Hash(int medicoId, string codigo) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"dpm-medico:{medicoId}:{codigo.Trim()}")));
}
