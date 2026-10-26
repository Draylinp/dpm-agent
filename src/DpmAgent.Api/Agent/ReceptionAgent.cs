using System.Globalization;
using System.Text.Json.Nodes;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;

namespace DpmAgent.Api.Agent;

public sealed record AgentStep(string Herramienta, string Argumentos, string Resultado);

public sealed record AgentReply(string SesionId, string Respuesta, IReadOnlyList<AgentStep> Pasos);

/// <summary>
/// Agente de recepción del consultorio: agenda, consulta y cancela citas,
/// registra pacientes y escala al personal humano. Nunca da orientación clínica.
/// </summary>
public sealed class ReceptionAgent
{
    private const int MaxIteraciones = 8;

    private readonly IChatModel _model;
    private readonly AgentTools _tools;
    private readonly ConversationStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReceptionAgent> _logger;

    public ReceptionAgent(
        IChatModel model,
        AgentTools tools,
        ConversationStore store,
        ILogger<ReceptionAgent> logger,
        TimeProvider? clock = null)
    {
        _model = model;
        _tools = tools;
        _store = store;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public Task<AgentReply> RunAsync(string? sesionId, string mensaje, CancellationToken ct) =>
        RunAsync(sesionId, mensaje, telefonoCanal: null, ct);

    /// <param name="telefonoCanal">
    /// Teléfono que el canal garantiza (el remitente de WhatsApp o el número simulado del panel).
    /// Es lo único que permite identificar al paciente.
    /// </param>
    public async Task<AgentReply> RunAsync(string? sesionId, string mensaje, string? telefonoCanal, CancellationToken ct)
    {
        sesionId = string.IsNullOrWhiteSpace(sesionId) ? Guid.NewGuid().ToString("N")[..12] : sesionId.Trim();

        var sesion = _store.Get(sesionId);
        sesion.VincularTelefono(telefonoCanal);

        var historial = sesion.Mensajes;
        historial.Add(Message("user", mensaje));
        var pasos = new List<AgentStep>();

        for (var i = 0; i < MaxIteraciones; i++)
        {
            var enviados = new List<JsonObject> { Message("system", SystemPrompt()) };
            enviados.AddRange(historial);

            var respuesta = await _model.CompleteAsync(enviados, _tools.Definitions, ct);
            respuesta["role"] = "assistant";
            historial.Add(respuesta);

            if (respuesta["tool_calls"] is not JsonArray llamadas || llamadas.Count == 0)
            {
                var texto = respuesta["content"]?.ToString()?.Trim();
                _store.Save(sesion);
                return new AgentReply(sesionId, string.IsNullOrEmpty(texto) ? "¿En qué más le puedo ayudar?" : texto, pasos);
            }

            foreach (var llamada in llamadas.OfType<JsonObject>())
            {
                var id = llamada["id"]?.ToString() ?? Guid.NewGuid().ToString("N");
                var nombre = llamada["function"]?["name"]?.ToString() ?? "";
                var args = llamada["function"]?["arguments"]?.ToString();

                var resultado = await _tools.ExecuteAsync(sesion, nombre, args, ct);
                _logger.LogInformation("Sesión {Sesion}: {Tool}({Args}) -> {Result}", sesionId, nombre, args, resultado);

                pasos.Add(new AgentStep(nombre, args ?? "{}", resultado));
                historial.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = id,
                    ["content"] = resultado,
                });
            }
        }

        _logger.LogWarning("Sesión {Sesion}: se alcanzó el límite de {Max} iteraciones.", sesionId, MaxIteraciones);
        _store.Save(sesion);
        return new AgentReply(
            sesionId,
            "Disculpe, no pude completar la solicitud. Voy a pasar su caso al personal del consultorio.",
            pasos);
    }

    private string SystemPrompt()
    {
        var ahora = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), MockDpmGateway.ZonaRd);
        var es = CultureInfo.GetCultureInfo("es-DO");

        return $"""
            Eres el asistente de recepción de un consultorio médico en República Dominicana.
            Fecha y hora actual: {ahora.ToString("dddd d 'de' MMMM 'de' yyyy, h:mm tt", es)} ({ahora:yyyy-MM-dd}).

            Tu trabajo es SOLO administrativo: agendar, consultar, reprogramar y cancelar citas,
            registrar pacientes nuevos y pasar casos al personal humano.

            Reglas obligatorias:
            1. Nunca des orientación médica: no interpretes síntomas, no sugieras diagnósticos,
               medicamentos, dosis ni tratamientos, y no comentes resultados de estudios.
               Si el paciente pregunta algo clínico, explica con amabilidad que eso lo responde el médico
               y usa escalar_a_personal.
            2. Si el paciente describe algo que pueda ser una emergencia (dolor de pecho, dificultad para respirar,
               sangrado abundante, pérdida de conciencia, etc.), dile que llame al 911 o vaya a emergencias de inmediato
               y usa escalar_a_personal con urgente = true.
            3. El paciente se identifica por el número desde el que escribe: al empezar usa buscar_paciente
               (no le pidas el teléfono). Si no aparece, pídele el nombre completo y usa registrar_paciente.
               Solo puedes ver, crear o cancelar citas de ese paciente, nunca de otra persona.
            4. Antes de crear o cancelar una cita, confirma con el paciente el médico, el día y la hora.
            5. Usa siempre las herramientas para obtener datos reales. Nunca inventes horarios, ids ni pacientes.
            6. Para crear una cita, usa exactamente el valor fecha_hora que devolvió consultar_disponibilidad.
            7. En el motivo de la cita escribe solo algo administrativo ("primera consulta", "control"), sin detalles de salud.
            8. Responde en español dominicano neutro, cordial y breve, como un mensaje de WhatsApp.
               Al ofrecer horarios, muestra como máximo 4 opciones.
            """;
    }

    private static JsonObject Message(string role, string content) => new()
    {
        ["role"] = role,
        ["content"] = content,
    };
}
