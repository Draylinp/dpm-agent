using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Model;

namespace DpmAgent.Tests;

/// <summary>Reloj fijo para pruebas deterministas.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}

/// <summary>
/// Modelo falso: cada llamada ejecuta el siguiente paso del guion
/// y guarda los mensajes que recibió.
/// </summary>
public sealed class ScriptedChatModel : IChatModel
{
    private readonly Queue<Func<IReadOnlyList<JsonObject>, JsonObject>> _guion = new();

    public List<IReadOnlyList<JsonObject>> Llamadas { get; } = [];

    public ScriptedChatModel Then(Func<IReadOnlyList<JsonObject>, JsonObject> paso)
    {
        _guion.Enqueue(paso);
        return this;
    }

    public ScriptedChatModel ThenTool(string nombre, object args) =>
        Then(_ => ToolCall(nombre, args));

    public ScriptedChatModel ThenText(string texto) =>
        Then(_ => new JsonObject { ["role"] = "assistant", ["content"] = texto });

    public Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
    {
        Llamadas.Add(messages.Select(m => m.DeepClone().AsObject()).ToList());
        if (_guion.Count == 0)
            throw new InvalidOperationException("El guion del modelo falso se agotó.");
        return Task.FromResult(_guion.Dequeue()(messages));
    }

    public static JsonObject ToolCall(string nombre, object args) => new()
    {
        ["role"] = "assistant",
        ["content"] = null,
        ["tool_calls"] = new JsonArray(new JsonObject
        {
            ["id"] = $"call_{nombre}_{Guid.NewGuid():N}",
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = nombre,
                ["arguments"] = JsonSerializer.Serialize(args),
            },
        }),
    };
}
