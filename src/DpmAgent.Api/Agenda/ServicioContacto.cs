using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Escalamiento = DpmAgent.Api.Data.Entidades.Escalamiento;

namespace DpmAgent.Api.Agenda;

public sealed class ContactoOptions
{
    public const string SectionName = "Contacto";

    /// <summary>Mensajes seguidos que fallan (después de sus reintentos) para marcar el número como no localizable.</summary>
    public int FallosParaNoLocalizable { get; set; } = 3;
}

/// <summary>
/// Si se puede contactar a un paciente: números que fallan o que no son de la persona (no localizable),
/// confirmación del número en seguimientos lejanos y pacientes desactivados por la secretaria.
/// </summary>
public sealed class ServicioContacto(DpmDbContext db, Bitacora bitacora, TimeProvider clock)
{
    private DateTime Ahora => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Después de que un mensaje quedó "fallido": si los últimos N mensajes a ese número fallaron, sus pacientes quedan
    /// como no localizables y la secretaria recibe un escalamiento. Lo llama el despachador (sin consultorio en el ámbito).
    /// </summary>
    public static async Task<bool> RegistrarFalloAsync(DpmDbContext db, int consultorioId, string destino, int umbral, DateTime ahora, CancellationToken ct)
    {
        var ultimos = await db.MensajesSalientes.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.ConsultorioId == consultorioId && m.Destino == destino && (m.Estado == "enviado" || m.Estado == "fallido"))
            .OrderByDescending(m => m.Id).Take(Math.Max(1, umbral)).Select(m => m.Estado).ToListAsync(ct);
        if (ultimos.Count < umbral || ultimos.Any(e => e != "fallido"))
            return false;

        var pacientes = await db.Pacientes.IgnoreQueryFilters()
            .Where(p => p.ConsultorioId == consultorioId && p.Telefono == destino && p.Activo && p.NoLocalizableUtc == null).ToListAsync(ct);
        if (pacientes.Count == 0)
            return false;
        foreach (var p in pacientes)
        {
            p.NoLocalizableUtc = ahora;
            db.Escalamientos.Add(new Escalamiento
            {
                ConsultorioId = consultorioId, SesionId = Canales.CanalesConocidos.SesionWhatsApp(destino), PacienteId = p.Id,
                Motivo = $"Contacto no localizable: los últimos {umbral} mensajes a su número fallaron. Llamar o actualizar el número.",
                MotivoEn = $"Unreachable contact: the last {umbral} messages to this number failed. Call or update the number.",
                Urgente = false, Estado = "pendiente", CreadoUtc = ahora,
            });
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>El paciente volvió a escribir desde su número: es localizable otra vez.</summary>
    public async Task MarcarLocalizableAsync(string telefono, CancellationToken ct)
    {
        var normalizado = Telefono.Normalizar(telefono);
        await db.Pacientes.Where(p => p.Telefono == normalizado && p.NoLocalizableUtc != null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.NoLocalizableUtc, (DateTime?)null), ct);
    }

    /// <summary>
    /// Respuesta al mensaje que confirma el número de un seguimiento lejano. Si no es la persona, el paciente queda no
    /// localizable, sus seguimientos abiertos pasan a la secretaria y no se le dice nada más a quien respondió.
    /// </summary>
    public async Task<int> ConfirmarNumeroAsync(int pacienteId, bool correcto, string? sesionId, CancellationToken ct)
    {
        var abiertos = await db.Seguimientos.Where(s => s.PacienteId == pacienteId && s.Estado == ServicioSeguimientos.Contactando).ToListAsync(ct);
        if (correcto)
        {
            foreach (var s in abiertos)
                s.NumeroConfirmadoUtc = Ahora;
            await db.SaveChangesAsync(ct);
            return abiertos.Count;
        }

        var p = await db.Pacientes.FirstAsync(x => x.Id == pacienteId, ct);
        p.NoLocalizableUtc = Ahora;
        foreach (var s in abiertos)
            s.Estado = ServicioSeguimientos.SinRespuesta;
        db.Escalamientos.Add(new Escalamiento
        {
            ConsultorioId = db.ConsultorioId, SesionId = sesionId ?? "", PacienteId = pacienteId,
            Motivo = "Quien respondió dice que el número ya no es del paciente. Actualizar su teléfono.",
            MotivoEn = "Whoever answered says the number no longer belongs to the patient. Update their phone.",
            Urgente = false, Estado = "pendiente", CreadoUtc = Ahora,
        });
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(Bitacora.Agente, "numero_incorrecto", "paciente", IdsDpm.Paciente(pacienteId), sesionId, null, ct);
        return abiertos.Count;
    }

    /// <summary>Desactivar (se mudó, falleció, pidió no volver): sin mensajes, seguimientos y lista de espera cancelados.</summary>
    public async Task<bool> DesactivarAsync(int pacienteId, string actor, CancellationToken ct)
    {
        var p = await db.Pacientes.FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null)
            return false;
        p.Activo = false;
        p.DesactivadoUtc = Ahora;
        await db.Seguimientos.Where(s => s.PacienteId == pacienteId && s.Estado != ServicioSeguimientos.Cancelado && s.Estado != ServicioSeguimientos.Agendado)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, ServicioSeguimientos.Cancelado), ct);
        await db.ListaEspera.Where(l => l.PacienteId == pacienteId && l.Estado == "activa")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, "cancelada"), ct);
        await db.SolicitudesSobrecupo.Where(s => s.PacienteId == pacienteId && s.Estado == ServicioUrgencias.Pendiente)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Estado, "cancelada"), ct);
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, "paciente_desactivado", "paciente", IdsDpm.Paciente(pacienteId), null, null, ct);
        return true;
    }

    public async Task<bool> ReactivarAsync(int pacienteId, string actor, CancellationToken ct)
    {
        var p = await db.Pacientes.FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null)
            return false;
        p.Activo = true;
        p.DesactivadoUtc = null;
        p.NoLocalizableUtc = null;
        await db.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync(actor, "paciente_reactivado", "paciente", IdsDpm.Paciente(pacienteId), null, null, ct);
        return true;
    }
}
