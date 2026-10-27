using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Agent;

/// <summary>
/// Estado de una conversación. <see cref="TelefonoCanal"/> lo fija el canal (WhatsApp o el panel),
/// nunca el modelo. <see cref="PacienteId"/> solo lo fijan las herramientas de identificación
/// a partir de ese teléfono, y es el único paciente sobre el que la sesión puede actuar.
/// </summary>
public sealed class Sesion
{
    public Sesion(string id) => Id = id;

    public string Id { get; }

    public string? TelefonoCanal { get; internal set; }

    public string? PacienteId { get; set; }

    /// <summary>Idioma del paciente (BCP-47). Se actualiza con cada mensaje en que se detecta con claridad.</summary>
    public string? Idioma { get; set; }

    /// <summary>1 si el idioma es español, inglés o kreyòl; 2 si es otro.</summary>
    public int NivelIdioma => Idiomas.Idiomas.Nivel(Idioma);

    /// <summary>Texto del último mensaje del paciente en este turno (sin guardar), para los escalamientos.</summary>
    public string? UltimoMensajePaciente { get; internal set; }

    /// <summary>Cuántos mensajes del paciente lleva la conversación. Sube al empezar cada turno.</summary>
    public int Turno { get; internal set; }

    /// <summary>
    /// Cita cuya cancelación propuso el agente y en qué turno. Solo se ejecuta en un turno posterior,
    /// es decir, después de un mensaje nuevo del paciente.
    /// </summary>
    public string? CancelacionPropuesta { get; set; }

    public int? CancelacionPropuestaTurno { get; set; }

    /// <summary>
    /// Turno en que se mostró el texto de consentimiento y su versión. Como cancelar, la respuesta solo
    /// se registra en un turno posterior: tiene que venir de un mensaje real del paciente.
    /// </summary>
    public int? ConsentimientoOfrecidoTurno { get; set; }

    public string? ConsentimientoVersion { get; set; }

    /// <summary>Texto fijo que el servidor agrega al final de la respuesta de este turno (no lo escribe el modelo).</summary>
    internal string? AnexoRespuesta { get; set; }

    public List<JsonObject> Mensajes { get; set; } = [];

    /// <summary>Cuántos de <see cref="Mensajes"/> ya estaban guardados al cargar la sesión.</summary>
    internal int MensajesGuardados { get; set; }

    /// <summary>
    /// Asocia el teléfono del canal a la sesión. Una sesión no puede cambiar de teléfono:
    /// así nadie puede tomar una sesión ajena enviando otro número.
    /// </summary>
    public void VincularTelefono(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono))
            return;

        var normalizado = Telefono.Normalizar(telefono)
            ?? throw new SesionInvalidaException("El teléfono del canal no es válido.");

        if (TelefonoCanal is null)
            TelefonoCanal = normalizado;
        else if (TelefonoCanal != normalizado)
            throw new SesionInvalidaException("Esta conversación pertenece a otro número de teléfono.");
    }

    /// <summary>Empieza un turno nuevo: llegó un mensaje del paciente.</summary>
    internal void NuevoTurno() => Turno++;

    internal Sesion Copia() => new(Id)
    {
        TelefonoCanal = TelefonoCanal,
        PacienteId = PacienteId,
        Idioma = Idioma,
        Turno = Turno,
        CancelacionPropuesta = CancelacionPropuesta,
        CancelacionPropuestaTurno = CancelacionPropuestaTurno,
        ConsentimientoOfrecidoTurno = ConsentimientoOfrecidoTurno,
        ConsentimientoVersion = ConsentimientoVersion,
        Mensajes = new List<JsonObject>(Mensajes),
        MensajesGuardados = Mensajes.Count,
    };
}

public sealed class SesionInvalidaException(string message) : Exception(message);

/// <summary>Historial y estado de las conversaciones, por sesión.</summary>
public interface IConversationStore
{
    /// <summary>Devuelve el estado de la sesión (vacío si es nueva). Los cambios se aplican con <see cref="GuardarAsync"/>.</summary>
    Task<Sesion> ObtenerAsync(string sesionId, CancellationToken ct);

    Task GuardarAsync(Sesion sesion, CancellationToken ct);
}

/// <summary>
/// Historial en memoria, para pruebas y para correr sin base de datos.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    internal const int MaxMensajes = 60;

    private readonly ConcurrentDictionary<string, Sesion> _sesiones = new();

    public Task<Sesion> ObtenerAsync(string sesionId, CancellationToken ct) =>
        Task.FromResult(_sesiones.GetOrAdd(sesionId, id => new Sesion(id)).Copia());

    public Task GuardarAsync(Sesion sesion, CancellationToken ct)
    {
        sesion.Mensajes = Recortar(sesion.Mensajes);
        _sesiones[sesion.Id] = sesion.Copia();
        return Task.CompletedTask;
    }

    /// <summary>Deja los últimos mensajes sin dejar respuestas de herramientas huérfanas al inicio.</summary>
    internal static List<JsonObject> Recortar(List<JsonObject> mensajes)
    {
        if (mensajes.Count <= MaxMensajes)
            return mensajes;

        var corte = mensajes.Count - MaxMensajes;
        while (corte < mensajes.Count && mensajes[corte]["role"]?.ToString() != "user")
            corte++;
        return mensajes.Skip(corte).ToList();
    }
}
