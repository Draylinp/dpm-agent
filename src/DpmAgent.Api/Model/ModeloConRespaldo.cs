using System.Net;
using System.Text.Json.Nodes;

namespace DpmAgent.Api.Model;

/// <summary>
/// Usa el modelo principal y, si no está disponible (sin clave, caído, saturado o lento),
/// repite la misma petición con el de respaldo. Los errores de la petición en sí (400, 401...)
/// no se reintentan: fallarían igual.
/// </summary>
public sealed class ModeloConRespaldo : IChatModel
{
    private readonly IChatModel _principal;
    private readonly IChatModel _respaldo;
    private readonly ILogger<ModeloConRespaldo> _logger;

    public ModeloConRespaldo(IChatModel principal, IChatModel respaldo, ILogger<ModeloConRespaldo> logger)
    {
        _principal = principal;
        _respaldo = respaldo;
        _logger = logger;
    }

    public async Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
    {
        try
        {
            return await _principal.CompleteAsync(messages, tools, ct);
        }
        catch (Exception ex) when (DebeUsarRespaldo(ex, ct))
        {
            _logger.LogWarning("El modelo principal no respondió ({Motivo}); se usa el de respaldo.", ex.Message);
            return await _respaldo.CompleteAsync(messages, tools, ct);
        }
    }

    internal static bool DebeUsarRespaldo(Exception ex, CancellationToken ct) => ex switch
    {
        // El cliente canceló: no hay a quién responder.
        OperationCanceledException when ct.IsCancellationRequested => false,
        // Tiempo de espera del HttpClient.
        TaskCanceledException => true,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests } => true,
        HttpRequestException { StatusCode: { } s } => (int)s >= 500,
        // Proveedor sin configurar (por ejemplo, falta la clave).
        InvalidOperationException => true,
        _ => false,
    };
}
