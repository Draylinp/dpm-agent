using System.Text.Json.Nodes;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

/// <summary>
/// Envía mensajes por iniciativa del sistema: los agrega al historial de la conversación del paciente
/// (para que el agente entienda su respuesta) y los encola para el despachador.
/// </summary>
public sealed class Notificador
{
    private readonly DpmDbContext _db;
    private readonly IConversationStore _conversaciones;
    private readonly DespertadorAgenda _despertador;
    private readonly TimeProvider _clock;

    public Notificador(DpmDbContext db, IConversationStore conversaciones, DespertadorAgenda despertador, TimeProvider clock)
    {
        _db = db;
        _conversaciones = conversaciones;
        _despertador = despertador;
        _clock = clock;
    }

    /// <summary>Encola un mensaje para el paciente en su conversación más reciente (o en WhatsApp si no tiene).</summary>
    public async Task<string> NotificarPacienteAsync(int pacienteId, string texto, string tipo, CancellationToken ct)
    {
        var paciente = await _db.Pacientes.AsNoTracking().FirstAsync(p => p.Id == pacienteId, ct);

        var sesionId = await _db.Conversaciones.AsNoTracking()
                           .Where(c => c.PacienteId == pacienteId)
                           .OrderByDescending(c => c.ActualizadaUtc)
                           .Select(c => c.SesionId)
                           .FirstOrDefaultAsync(ct)
                       ?? CanalesConocidos.SesionWhatsApp(paciente.Telefono);

        var sesion = await _conversaciones.ObtenerAsync(sesionId, ct);
        sesion.VincularTelefono(paciente.Telefono);
        sesion.PacienteId ??= IdsDpm.Paciente(pacienteId);
        sesion.Mensajes.Add(new JsonObject { ["role"] = "assistant", ["content"] = texto });
        await _conversaciones.GuardarAsync(sesion, ct);

        _db.MensajesSalientes.Add(new MensajeSaliente
        {
            ConsultorioId = _db.ConsultorioId,
            SesionId = sesionId,
            Canal = CanalesConocidos.DeSesion(sesionId),
            Destino = paciente.Telefono,
            Texto = texto,
            Tipo = tipo,
            Estado = "pendiente",
            CreadoUtc = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);

        _despertador.Despertar();
        return sesionId;
    }
}
