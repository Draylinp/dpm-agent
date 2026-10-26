using System.Text.Json.Nodes;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using E = DpmAgent.Api.Data.Entidades;

namespace DpmAgent.Api.Agent;

/// <summary>
/// Conversaciones guardadas en la base de datos. El historial completo se conserva
/// (solo se agregan mensajes); al modelo se le envían los más recientes.
/// </summary>
public sealed class DbConversationStore : IConversationStore
{
    private const int MensajesACargar = 200;

    private readonly DpmDbContext _db;
    private readonly TimeProvider _clock;

    public DbConversationStore(DpmDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<Sesion> ObtenerAsync(string sesionId, CancellationToken ct)
    {
        var conv = await _db.Conversaciones.AsNoTracking().FirstOrDefaultAsync(c => c.SesionId == sesionId, ct);
        if (conv is null)
            return new Sesion(sesionId);

        var recientes = await _db.Mensajes.AsNoTracking()
            .Where(m => m.ConversacionId == conv.Id)
            .OrderByDescending(m => m.Id)
            .Take(MensajesACargar)
            .Select(m => m.Json)
            .ToListAsync(ct);

        recientes.Reverse();
        var mensajes = InMemoryConversationStore.Recortar(
            recientes.Select(j => JsonNode.Parse(j)!.AsObject()).ToList());

        return new Sesion(sesionId)
        {
            TelefonoCanal = conv.TelefonoCanal,
            PacienteId = conv.PacienteId is { } p ? IdsDpm.Paciente(p) : null,
            Idioma = conv.Idioma,
            Turno = conv.Turno,
            CancelacionPropuesta = conv.CancelacionPropuesta,
            CancelacionPropuestaTurno = conv.CancelacionPropuestaTurno,
            Mensajes = mensajes,
            MensajesGuardados = mensajes.Count,
        };
    }

    public async Task GuardarAsync(Sesion sesion, CancellationToken ct)
    {
        var ahora = _clock.GetUtcNow().UtcDateTime;
        var conv = await _db.Conversaciones.FirstOrDefaultAsync(c => c.SesionId == sesion.Id, ct);
        if (conv is null)
        {
            conv = new E.Conversacion { ConsultorioId = _db.ConsultorioId, SesionId = sesion.Id, CreadaUtc = ahora };
            _db.Conversaciones.Add(conv);
        }

        conv.TelefonoCanal = sesion.TelefonoCanal;
        conv.PacienteId = IdsDpm.Leer(sesion.PacienteId, "PAC");
        conv.Idioma = sesion.Idioma;
        conv.NivelIdioma = sesion.Idioma is null ? 0 : sesion.NivelIdioma;
        conv.Turno = sesion.Turno;
        conv.CancelacionPropuesta = sesion.CancelacionPropuesta;
        conv.CancelacionPropuestaTurno = sesion.CancelacionPropuestaTurno;
        conv.ActualizadaUtc = ahora;

        foreach (var m in sesion.Mensajes.Skip(sesion.MensajesGuardados))
        {
            conv.Mensajes.Add(new E.Mensaje
            {
                Rol = m["role"]?.ToString() ?? "desconocido",
                Json = m.ToJsonString(),
                CreadoUtc = ahora,
            });
        }

        await _db.SaveChangesAsync(ct);
        sesion.MensajesGuardados = sesion.Mensajes.Count;

        // El idioma del paciente sirve para los mensajes automáticos aunque la conversación se borre.
        if (conv.PacienteId is { } paciente && sesion.Idioma is { } idioma)
        {
            await _db.Pacientes.Where(p => p.Id == paciente && p.Idioma != idioma)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Idioma, idioma), ct);
        }
    }
}
