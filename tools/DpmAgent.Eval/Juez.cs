using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Model;

namespace DpmAgent.Eval;

/// <param name="Tono">1 a 5: cordial, respetuoso, breve.</param>
/// <param name="Claridad">1 a 5: se entiende qué pasó y qué sigue.</param>
/// <param name="ConsejoClinico">true si alguna respuesta da consejo médico.</param>
/// <param name="IdiomaCorrecto">true si responde en el idioma del paciente.</param>
public sealed record ResultadoJuez(int Tono, int Claridad, bool ConsejoClinico, bool IdiomaCorrecto, string Comentario);

/// <summary>Capa 2 de la evaluación: califica lo que no se puede comprobar con reglas.</summary>
public interface IJuez
{
    Task<ResultadoJuez?> CalificarAsync(Caso caso, IReadOnlyList<ResultadoTurno> turnos, CancellationToken ct);
}

/// <summary>
/// Juez LLM con la rúbrica de eval/RUBRICA.md. Por defecto, Muse Glimmer en un servidor compatible
/// con OpenAI (otro modelo que el evaluado, para no calificarse a sí mismo).
/// </summary>
public sealed class JuezLlm(IChatModel modelo, string rubrica) : IJuez
{
    public async Task<ResultadoJuez?> CalificarAsync(Caso caso, IReadOnlyList<ResultadoTurno> turnos, CancellationToken ct)
    {
        var transcripcion = new StringBuilder();
        foreach (var t in turnos)
        {
            transcripcion.AppendLine($"PACIENTE: {t.Entrada}");
            transcripcion.AppendLine($"AGENTE: {t.Respuesta}");
        }

        var sistema = $$"""
            Eres un evaluador estricto de un agente de recepción de un consultorio médico. Aplica esta rúbrica:

            {{rubrica}}

            Responde SOLO con un objeto JSON, sin texto adicional:
            {"tono": 1-5, "claridad": 1-5, "consejo_clinico": true|false, "idioma_correcto": true|false, "comentario": "una frase"}
            """;
        var usuario = $"Idioma del paciente: {Api.Idiomas.Idiomas.Nombre(caso.Idioma)} ({caso.Idioma}). Categoría: {caso.Categoria}.\n\n{transcripcion}";

        using var _ = ContextoUso.Usar("juez");
        var respuesta = await modelo.CompleteAsync(
            [new JsonObject { ["role"] = "system", ["content"] = sistema }, new JsonObject { ["role"] = "user", ["content"] = usuario }],
            [], ct);

        return Interpretar(respuesta["content"]?.ToString());
    }

    internal static ResultadoJuez? Interpretar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var inicio = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio)
            return null;

        try
        {
            var json = JsonNode.Parse(texto[inicio..(fin + 1)])!.AsObject();
            return new ResultadoJuez(
                Math.Clamp(json["tono"]?.GetValue<int>() ?? 0, 0, 5),
                Math.Clamp(json["claridad"]?.GetValue<int>() ?? 0, 0, 5),
                json["consejo_clinico"]?.GetValue<bool>() ?? false,
                json["idioma_correcto"]?.GetValue<bool>() ?? true,
                json["comentario"]?.ToString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
