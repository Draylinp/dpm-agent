using System.Text.Json.Nodes;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Cumplimiento;

public sealed record ResultadoBorrado(
    int Conversaciones, int Mensajes, int Consentimientos, int Carnets, int Escalamientos, int Citas, int AvisosYMensajesCanal, int RegistrosBitacoraAnonimizados);

/// <summary>
/// Derechos del paciente sobre sus datos (Ley 172-13 de República Dominicana, como buena práctica técnica):
/// la secretaria exporta (JSON) o borra todo lo que el sistema guarda de un paciente, a petición suya.
/// </summary>
public sealed class ServicioPrivacidad(DpmDbContext db, Bitacora bitacora, TimeProvider clock)
{
    public async Task<JsonObject?> ExportarAsync(int pacienteId, string actor, CancellationToken ct)
    {
        var p = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null)
            return null;

        var sesiones = await SesionesAsync(pacienteId, p.Telefono, ct);
        var conversaciones = await db.Conversaciones.AsNoTracking().Where(c => sesiones.Contains(c.SesionId))
            .Select(c => new { c.Id, c.CreadaUtc, c.Idioma }).ToListAsync(ct);
        var idsConv = conversaciones.Select(c => c.Id).ToList();
        var mensajes = await db.Mensajes.AsNoTracking().Where(m => idsConv.Contains(m.ConversacionId) && (m.Rol == "user" || m.Rol == "assistant"))
            .OrderBy(m => m.Id).Select(m => new { m.ConversacionId, m.Rol, m.Json, m.CreadoUtc }).ToListAsync(ct);

        var citas = await db.Citas.AsNoTracking().Where(c => c.PacienteId == pacienteId).Include(c => c.Medico).OrderBy(c => c.FechaHoraUtc).ToListAsync(ct);
        var consentimientos = await db.Consentimientos.AsNoTracking().Where(c => c.PacienteId == pacienteId).OrderBy(c => c.FechaUtc).ToListAsync(ct);
        var escalamientos = await db.Escalamientos.AsNoTracking().Where(e => e.PacienteId == pacienteId || sesiones.Contains(e.SesionId)).ToListAsync(ct);
        var carnets = await db.CarnetsPendientes.AsNoTracking().Where(c => c.PacienteId == pacienteId || sesiones.Contains(c.SesionId)).ToListAsync(ct);
        var lista = await db.ListaEspera.AsNoTracking().Where(l => l.PacienteId == pacienteId).ToListAsync(ct);
        var autorizaciones = await db.AutorizacionesArs.AsNoTracking().Where(a => a.PacienteId == pacienteId).ToListAsync(ct);
        var avisos = await db.MensajesSalientes.AsNoTracking().Where(m => m.Destino == p.Telefono && m.Tipo != "respuesta").OrderBy(m => m.Id).ToListAsync(ct);

        var json = new JsonObject
        {
            ["generado"] = clock.GetUtcNow().ToString("O"),
            ["paciente"] = new JsonObject
            {
                ["id"] = IdsDpm.Paciente(p.Id), ["nombre"] = p.Nombre, ["telefono"] = p.Telefono, ["ars"] = p.Ars,
                ["numeroAfiliado"] = p.NumeroAfiliado, ["idioma"] = p.Idioma, ["registrado"] = Fecha(p.CreadoUtc),
            },
            ["citas"] = new JsonArray(citas.Select(c => (JsonNode)new JsonObject
            {
                ["id"] = IdsDpm.Cita(c.Id), ["fechaHora"] = Fecha(c.FechaHoraUtc), ["medico"] = c.Medico?.Nombre, ["motivo"] = c.Motivo, ["estado"] = c.Estado,
            }).ToArray()),
            ["listaEspera"] = new JsonArray(lista.Select(l => (JsonNode)new JsonObject
            {
                ["desde"] = l.Desde.ToString("yyyy-MM-dd"), ["hasta"] = l.Hasta.ToString("yyyy-MM-dd"), ["estado"] = l.Estado,
            }).ToArray()),
            ["consentimientos"] = new JsonArray(consentimientos.Select(c => (JsonNode)new JsonObject
            {
                ["categoria"] = c.Categoria, ["estado"] = c.Estado, ["canal"] = c.Canal, ["versionTexto"] = c.VersionTexto, ["fecha"] = Fecha(c.FechaUtc),
            }).ToArray()),
            ["conversaciones"] = new JsonArray(conversaciones.Select(c => (JsonNode)new JsonObject
            {
                ["inicio"] = Fecha(c.CreadaUtc),
                ["idioma"] = c.Idioma,
                ["mensajes"] = new JsonArray(mensajes.Where(m => m.ConversacionId == c.Id).Select(m => (JsonNode)new JsonObject
                {
                    ["de"] = m.Rol == "user" ? "paciente" : "consultorio",
                    ["texto"] = JsonNode.Parse(m.Json)?["content"]?.ToString(),
                    ["fecha"] = Fecha(m.CreadoUtc),
                }).Where(m => !string.IsNullOrEmpty(m!["texto"]?.ToString())).ToArray()),
            }).ToArray()),
            ["escalamientos"] = new JsonArray(escalamientos.Select(e => (JsonNode)new JsonObject
            {
                ["fecha"] = Fecha(e.CreadoUtc), ["motivo"] = e.Motivo, ["mensajeOriginal"] = e.MensajeOriginal, ["estado"] = e.Estado,
            }).ToArray()),
            ["carnets"] = new JsonArray(carnets.Select(c => (JsonNode)new JsonObject
            {
                ["fecha"] = Fecha(c.CreadoUtc), ["nombre"] = c.Nombre, ["ars"] = c.Ars, ["numeroAfiliado"] = c.NumeroAfiliado, ["estado"] = c.Estado,
            }).ToArray()),
            ["autorizacionesArs"] = new JsonArray(autorizaciones.Select(a => (JsonNode)new JsonObject
            {
                ["cita"] = IdsDpm.Cita(a.CitaId), ["estado"] = a.Estado, ["contenido"] = a.Contenido,
            }).ToArray()),
            ["mensajesAutomaticos"] = new JsonArray(avisos.Select(m => (JsonNode)new JsonObject
            {
                ["tipo"] = m.Tipo, ["texto"] = m.Texto, ["estado"] = m.Estado, ["fecha"] = Fecha(m.CreadoUtc),
            }).ToArray()),
        };

        await bitacora.RegistrarAsync(actor, "datos_exportados", "paciente", IdsDpm.Paciente(pacienteId), null, null, ct);
        return json;
    }

    /// <summary>
    /// Borra al paciente y todo lo suyo: conversaciones, mensajes, consentimientos, carnets, escalamientos, citas,
    /// lista de espera, autorizaciones y mensajes de canal. La bitácora conserva solo qué se hizo y cuándo,
    /// sin datos personales.
    /// </summary>
    public async Task<ResultadoBorrado?> BorrarAsync(int pacienteId, string actor, CancellationToken ct)
    {
        var p = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pacienteId, ct);
        if (p is null)
            return null;

        var telefono = p.Telefono;
        var internacional = Telefono.Internacional(telefono) ?? telefono;
        var sesiones = await SesionesAsync(pacienteId, telefono, ct);
        var idPaciente = IdsDpm.Paciente(pacienteId);
        var idsCitas = await db.Citas.Where(c => c.PacienteId == pacienteId).Select(c => c.Id).ToListAsync(ct);
        var entidadesCita = idsCitas.Select(IdsDpm.Cita).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var idsConv = db.Conversaciones.Where(c => sesiones.Contains(c.SesionId)).Select(c => c.Id);
        var mensajes = await db.Mensajes.Where(m => idsConv.Contains(m.ConversacionId)).ExecuteDeleteAsync(ct);
        var conversaciones = await db.Conversaciones.Where(c => sesiones.Contains(c.SesionId)).ExecuteDeleteAsync(ct);
        var canal = await db.MensajesSalientes.Where(m => m.Destino == telefono || sesiones.Contains(m.SesionId)).ExecuteDeleteAsync(ct)
                    + await db.MensajesEntrantes.Where(m => m.Telefono == telefono || m.Telefono == internacional).ExecuteDeleteAsync(ct);
        var consentimientos = await db.Consentimientos.Where(c => c.PacienteId == pacienteId).ExecuteDeleteAsync(ct);
        var carnets = await db.CarnetsPendientes
            .Where(c => c.PacienteId == pacienteId || c.TelefonoCanal == telefono || sesiones.Contains(c.SesionId)).ExecuteDeleteAsync(ct);
        var escalamientos = await db.Escalamientos.Where(e => e.PacienteId == pacienteId || sesiones.Contains(e.SesionId)).ExecuteDeleteAsync(ct);
        await db.OfertasHueco.Where(o => o.PacienteId == pacienteId).ExecuteDeleteAsync(ct);
        await db.ListaEspera.Where(l => l.PacienteId == pacienteId).ExecuteDeleteAsync(ct);
        await db.AutorizacionesArs.Where(a => a.PacienteId == pacienteId).ExecuteDeleteAsync(ct);
        var citas = await db.Citas.Where(c => c.PacienteId == pacienteId).ExecuteDeleteAsync(ct);

        // La bitácora conserva las acciones (quién, qué, cuándo) pero sin nada que identifique al paciente.
        var bitacoraAnonimizada = await db.Bitacora
            .Where(b => (b.SesionId != null && sesiones.Contains(b.SesionId)) || b.EntidadId == idPaciente || entidadesCita.Contains(b.EntidadId!)
                        || (b.Detalle != null && b.Detalle.Contains(idPaciente)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.SesionId, (string?)null)
                .SetProperty(b => b.EntidadId, (string?)null)
                .SetProperty(b => b.Detalle, (string?)null)
                .SetProperty(b => b.TextoOriginal, (string?)null)
                .SetProperty(b => b.ResumenEs, (string?)null)
                .SetProperty(b => b.ResumenEn, (string?)null), ct);

        await db.Pacientes.Where(x => x.Id == pacienteId).ExecuteDeleteAsync(ct);
        await tx.CommitAsync(ct);

        await bitacora.RegistrarAsync(actor, "datos_borrados", "paciente", null, null, null, ct);
        return new ResultadoBorrado(conversaciones, mensajes, consentimientos, carnets, escalamientos, citas, canal, bitacoraAnonimizada);
    }

    /// <summary>Todas las sesiones del paciente: las vinculadas a él, las de su teléfono y la de WhatsApp.</summary>
    private async Task<List<string>> SesionesAsync(int pacienteId, string telefono, CancellationToken ct)
    {
        var sesiones = await db.Conversaciones.AsNoTracking()
            .Where(c => c.PacienteId == pacienteId || c.TelefonoCanal == telefono)
            .Select(c => c.SesionId).ToListAsync(ct);
        sesiones.Add(CanalesConocidos.SesionWhatsApp(telefono));
        return sesiones.Distinct().ToList();
    }

    private static string Fecha(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToString("O");
}
