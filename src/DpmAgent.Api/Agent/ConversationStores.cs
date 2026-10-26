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

    internal Sesion Copia() => new(Id)
    {
        TelefonoCanal = TelefonoCanal,
        PacienteId = PacienteId,
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
