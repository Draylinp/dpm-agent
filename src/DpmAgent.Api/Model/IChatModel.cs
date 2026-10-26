using System.Text.Json.Nodes;

namespace DpmAgent.Api.Model;

/// <summary>
/// Abstracción del modelo de lenguaje. Permite cambiar entre la Meta Model API
/// (Muse Spark) y un servidor propio con pesos abiertos (Muse Glimmer) sin tocar el agente,
/// y usar un modelo falso en las pruebas.
/// </summary>
public interface IChatModel
{
    /// <summary>
    /// Envía la conversación y devuelve el mensaje del asistente
    /// en formato chat-completions: { role, content, tool_calls? }.
    /// </summary>
    Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct);
}
