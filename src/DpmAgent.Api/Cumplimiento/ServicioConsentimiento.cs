using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Idiomas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Cumplimiento;

/// <summary>Ajustes de cumplimiento (sección "Cumplimiento").</summary>
public sealed class CumplimientoOptions
{
    public const string SectionName = "Cumplimiento";

    /// <summary>Dirección pública del aviso de privacidad (en producción, absoluta: https://...).</summary>
    public string UrlPrivacidad { get; set; } = "/privacidad.html";
}

public static class CategoriasConsentimiento
{
    public const string Recordatorios = "recordatorios";
    public const string Seguimientos = "seguimientos";
    public const string ListaEspera = "lista_espera";

    public static readonly string[] Todas = [Recordatorios, Seguimientos, ListaEspera];

    /// <summary>Del médico, no del paciente: resumen diario de su agenda.</summary>
    public const string ResumenMedico = "resumen_medico";

    /// <summary>Categoría de consentimiento que necesita cada tipo de mensaje proactivo.</summary>
    public static string? DeTipoMensaje(string tipo) => tipo switch
    {
        // Avisos sobre una cita que el paciente ya tiene: el bloqueo de la agenda, el retraso del médico, la ausencia.
        "recordatorio" or "reprogramar" or "retraso" or "ausencia" => Recordatorios,
        "oferta" or "aviso" => ListaEspera,
        "seguimiento" => Seguimientos,
        _ => null,
    };
}

public static class CanalesConsentimiento
{
    public const string WhatsApp = "whatsapp";
    public const string Web = "web";
    public const string Presencial = "presencial";
}

/// <summary>Estado vigente de una categoría para un paciente.</summary>
public sealed record EstadoConsentimiento(string Categoria, bool Otorgado, string? Canal, string? VersionTexto, DateTime? FechaUtc);

/// <summary>
/// Consentimiento (opt-in) para mensajes por iniciativa del consultorio. Sin uno vigente para la categoría,
/// no se envía nada de esa categoría. Cada cambio queda como registro nuevo con la versión del texto mostrado.
/// </summary>
public sealed class ServicioConsentimiento
{
    /// <summary>Cambiar el texto de consentimiento obliga a subir la versión.</summary>
    public const string VersionTexto = "2026-10-27";

    private readonly DpmDbContext _db;
    private readonly TimeProvider _clock;
    private readonly Bitacora _bitacora;
    private readonly CumplimientoOptions _opciones;

    public ServicioConsentimiento(DpmDbContext db, TimeProvider clock, Bitacora bitacora, IOptions<CumplimientoOptions> opciones)
    {
        _db = db;
        _clock = clock;
        _bitacora = bitacora;
        _opciones = opciones.Value;
    }

    public string UrlPrivacidad => _opciones.UrlPrivacidad;

    /// <summary>Texto que se le muestra al paciente: nombra al consultorio, dice qué recibirá y cómo darse de baja.</summary>
    public string Texto(string? idioma, string consultorio) =>
        Textos.Obtener("consentimiento.texto", idioma, consultorio, _opciones.UrlPrivacidad);

    public async Task<IReadOnlyList<EstadoConsentimiento>> EstadoAsync(int pacienteId, CancellationToken ct)
    {
        var registros = await _db.Consentimientos.AsNoTracking()
            .Where(c => c.PacienteId == pacienteId)
            .OrderByDescending(c => c.FechaUtc).ThenByDescending(c => c.Id)
            .ToListAsync(ct);

        return CategoriasConsentimiento.Todas.Select(cat =>
        {
            var ultimo = registros.FirstOrDefault(r => r.Categoria == cat);
            return new EstadoConsentimiento(cat, ultimo?.Estado == "otorgado", ultimo?.Canal, ultimo?.VersionTexto, ultimo?.FechaUtc);
        }).ToList();
    }

    public Task<bool> VigenteAsync(int pacienteId, string categoria, CancellationToken ct) => VigenteAsync(_db, pacienteId, categoria, ct);

    public static async Task<bool> VigenteAsync(DpmDbContext db, int pacienteId, string categoria, CancellationToken ct)
    {
        var ultimo = await db.Consentimientos.AsNoTracking()
            .Where(c => c.PacienteId == pacienteId && c.Categoria == categoria)
            .OrderByDescending(c => c.FechaUtc).ThenByDescending(c => c.Id)
            .Select(c => c.Estado)
            .FirstOrDefaultAsync(ct);
        return ultimo == "otorgado";
    }

    /// <summary>Registra un otorgamiento o una revocación para las categorías dadas (todas si no se indican).</summary>
    public async Task RegistrarAsync(
        int pacienteId, IEnumerable<string>? categorias, bool otorgado, string canal, string? idioma, string registradoPor, string? sesionId,
        CancellationToken ct, string? versionTexto = null)
    {
        var paciente = await _db.Pacientes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pacienteId, ct)
                       ?? throw new ArgumentException($"No existe el paciente {IdsDpm.Paciente(pacienteId)}.");

        var lista = (categorias ?? CategoriasConsentimiento.Todas).Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        if (lista.Count == 0 || lista.Any(c => !CategoriasConsentimiento.Todas.Contains(c)))
            throw new ArgumentException($"Categorías válidas: {string.Join(", ", CategoriasConsentimiento.Todas)}.");
        if (canal is not (CanalesConsentimiento.WhatsApp or CanalesConsentimiento.Web or CanalesConsentimiento.Presencial))
            throw new ArgumentException("Canal no válido.");

        var ahora = _clock.GetUtcNow().UtcDateTime;
        foreach (var categoria in lista)
        {
            _db.Consentimientos.Add(new Consentimiento
            {
                ConsultorioId = _db.ConsultorioId,
                PacienteId = pacienteId,
                Telefono = paciente.Telefono,
                Categoria = categoria,
                Estado = otorgado ? "otorgado" : "revocado",
                Canal = canal,
                VersionTexto = versionTexto ?? VersionTexto,
                Idioma = idioma,
                RegistradoPor = registradoPor.Length > 60 ? registradoPor[..60] : registradoPor,
                FechaUtc = ahora,
            });
        }

        await _db.SaveChangesAsync(ct);
        await _bitacora.RegistrarAsync(registradoPor, otorgado ? "consentimiento_otorgado" : "consentimiento_revocado", "paciente",
            IdsDpm.Paciente(pacienteId), sesionId, $"{string.Join(",", lista)} por {canal}", ct);
    }

    /// <summary>Baja de todo: se revocan las categorías que estaban otorgadas.</summary>
    public async Task<int> RevocarTodoAsync(int pacienteId, string canal, string? idioma, string registradoPor, string? sesionId, CancellationToken ct)
    {
        var otorgadas = (await EstadoAsync(pacienteId, ct)).Where(e => e.Otorgado).Select(e => e.Categoria).ToList();
        if (otorgadas.Count > 0)
            await RegistrarAsync(pacienteId, otorgadas, false, canal, idioma, registradoPor, sesionId, ct);
        return otorgadas.Count;
    }
}
