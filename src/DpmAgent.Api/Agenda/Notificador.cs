using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Cumplimiento;
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

    /// <summary>
    /// Encola un mensaje para el paciente en su conversación más reciente (o en WhatsApp si no tiene),
    /// en su idioma: el de esa conversación, si no el último que usó, si no el del consultorio.
    /// Sin consentimiento vigente para la categoría del mensaje no se envía nada: queda como "llamar"
    /// para que la secretaria contacte al paciente por teléfono.
    /// </summary>
    /// <param name="crear">Arma el mensaje en el idioma y con el nombre del consultorio que se le indiquen.</param>
    public async Task<string> NotificarPacienteAsync(int pacienteId, Func<string, string, MensajeAgenda> crear, string tipo, CancellationToken ct)
    {
        var paciente = await _db.Pacientes.AsNoTracking().FirstAsync(p => p.Id == pacienteId, ct);

        var ultima = await _db.Conversaciones.AsNoTracking()
            .Where(c => c.PacienteId == pacienteId)
            .OrderByDescending(c => c.ActualizadaUtc)
            .Select(c => new { c.SesionId, c.Idioma })
            .FirstOrDefaultAsync(ct);
        var sesionId = ultima?.SesionId ?? CanalesConocidos.SesionWhatsApp(paciente.Telefono);

        var consultorio = await _db.Consultorios.AsNoTracking()
            .Where(c => c.Id == _db.ConsultorioId).Select(c => new { c.Nombre, c.IdiomaPredeterminado }).FirstOrDefaultAsync(ct);
        var idioma = ultima?.Idioma ?? paciente.Idioma ?? consultorio?.IdiomaPredeterminado;
        var mensaje = crear(idioma ?? Idiomas.Idiomas.Espanol, string.IsNullOrWhiteSpace(consultorio?.Nombre) ? "DPM" : consultorio.Nombre);
        var texto = mensaje.Texto;

        if (CategoriasConsentimiento.DeTipoMensaje(tipo) is { } categoria &&
            !await ServicioConsentimiento.VigenteAsync(_db, pacienteId, categoria, ct))
        {
            // No sale ni entra al historial: el paciente no lo recibió.
            _db.MensajesSalientes.Add(new MensajeSaliente
            {
                ConsultorioId = _db.ConsultorioId,
                SesionId = sesionId,
                Canal = CanalesConocidos.DeSesion(sesionId),
                Destino = paciente.Telefono,
                Texto = texto,
                Tipo = tipo,
                Idioma = mensaje.Idioma,
                Plantilla = mensaje.Plantilla,
                ParametrosJson = JsonSerializer.Serialize(mensaje.Parametros),
                Estado = EstadoLlamar,
                Error = $"Sin consentimiento para {categoria}",
                CreadoUtc = _clock.GetUtcNow().UtcDateTime,
            });
            await _db.SaveChangesAsync(ct);
            return sesionId;
        }

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
            Idioma = mensaje.Idioma,
            Plantilla = mensaje.Plantilla,
            ParametrosJson = JsonSerializer.Serialize(mensaje.Parametros),
            Estado = "pendiente",
            CreadoUtc = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);

        _despertador.Despertar();
        return sesionId;
    }

    /// <summary>
    /// Encola la respuesta del agente a un mensaje que llegó por un canal externo (WhatsApp).
    /// No se agrega al historial: el agente ya la guardó.
    /// </summary>
    /// <summary>Estado de un aviso que no se envió por falta de consentimiento: la secretaria debe llamar.</summary>
    public const string EstadoLlamar = "llamar";

    /// <summary>La secretaria ya llamó al paciente por ese aviso.</summary>
    public const string EstadoLlamado = "llamado";

    public async Task EncolarRespuestaAsync(string sesionId, string telefono, string texto, string? idioma, CancellationToken ct)
    {
        _db.MensajesSalientes.Add(new MensajeSaliente
        {
            ConsultorioId = _db.ConsultorioId,
            SesionId = sesionId,
            Canal = CanalesConocidos.DeSesion(sesionId),
            Destino = Telefono.Normalizar(telefono) ?? telefono,
            Texto = texto.Length > 2000 ? texto[..2000] : texto,
            Tipo = "respuesta",
            Idioma = idioma,
            Estado = "pendiente",
            CreadoUtc = _clock.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);
        _despertador.Despertar();
    }
}
