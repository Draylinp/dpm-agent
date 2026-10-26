using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace DpmAgent.Api.Agent;

/// <summary>
/// Historial de conversaciones en memoria, por sesión.
/// Suficiente para el prototipo; luego se puede mover a la base de datos de DPM.
/// </summary>
public sealed class ConversationStore
{
    private const int MaxMensajes = 60;

    private readonly ConcurrentDictionary<string, List<JsonObject>> _sesiones = new();

    public List<JsonObject> Get(string sesionId) => _sesiones.GetOrAdd(sesionId, _ => []);

    public void Save(string sesionId, List<JsonObject> mensajes)
    {
        // Recorta mensajes antiguos sin dejar respuestas de herramientas huérfanas al inicio.
        if (mensajes.Count > MaxMensajes)
        {
            var corte = mensajes.Count - MaxMensajes;
            while (corte < mensajes.Count && mensajes[corte]["role"]?.ToString() != "user")
                corte++;
            mensajes = mensajes.Skip(corte).ToList();
        }

        _sesiones[sesionId] = mensajes;
    }
}
