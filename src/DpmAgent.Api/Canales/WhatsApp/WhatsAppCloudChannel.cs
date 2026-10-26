using DpmAgent.Api.Dpm;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Canales.WhatsApp;

/// <summary>
/// Entrega mensajes salientes por WhatsApp. Los que inicia el consultorio (ofertas, recordatorios)
/// usan la plantilla aprobada si está configurada; las respuestas van como texto.
/// </summary>
public sealed class WhatsAppCloudChannel : IMessageChannel
{
    private readonly IWhatsAppCloudApi _api;
    private readonly WhatsAppOptions _options;

    public WhatsAppCloudChannel(IWhatsAppCloudApi api, IOptions<WhatsAppOptions> options)
    {
        _api = api;
        _options = options.Value;
    }

    public string Canal => CanalesConocidos.WhatsApp;

    public Task EnviarAsync(MensajeParaEnviar mensaje, CancellationToken ct)
    {
        // Vacío cuenta como no configurado (docker compose pasa las variables vacías de .env).
        var remitente = mensaje.RemitenteId ?? (string.IsNullOrWhiteSpace(_options.PhoneNumberId) ? null : _options.PhoneNumberId)
                        ?? throw new InvalidOperationException("Falta el phone_number_id de WhatsApp del consultorio.");
        var destino = Telefono.Internacional(mensaje.Destino)
                      ?? throw new InvalidOperationException($"Número de destino no válido: {mensaje.Destino}.");

        if (mensaje.Plantilla is not null && _options.Plantillas.TryGetValue(mensaje.Plantilla, out var plantilla) &&
            !string.IsNullOrWhiteSpace(plantilla))
        {
            return _api.EnviarPlantillaAsync(remitente, destino, plantilla, IdiomaPlantilla(mensaje.Idioma), mensaje.Parametros ?? [], ct);
        }

        return _api.EnviarTextoAsync(remitente, destino, mensaje.Texto, ct);
    }

    /// <summary>Código de idioma de la plantilla; nivel 2 o sin mapeo, el de inglés.</summary>
    internal string IdiomaPlantilla(string? idioma)
    {
        var texto = Idiomas.Idiomas.ParaTextos(idioma);
        return _options.IdiomasPlantillas.TryGetValue(texto, out var codigo) && !string.IsNullOrWhiteSpace(codigo)
            ? codigo
            : _options.IdiomasPlantillas.GetValueOrDefault(Idiomas.Idiomas.Respaldo) ?? Idiomas.Idiomas.Respaldo;
    }
}
