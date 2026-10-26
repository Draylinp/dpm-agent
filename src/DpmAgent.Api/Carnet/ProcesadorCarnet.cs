using System.Text.Encodings.Web;
using System.Text.Json;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Carnet;

/// <summary>
/// Foto del carnet → validación → Muse Spark → carnet pendiente → el agente pide confirmación.
/// La usan el endpoint HTTP y WhatsApp. La imagen solo vive en memoria.
/// </summary>
public sealed class ProcesadorCarnet
{
    public const string PrefijoCarnet = "[Foto de carnet] ";

    private static readonly JsonSerializerOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly ILectorCarnet _lector;
    private readonly IDpmGateway _dpm;
    private readonly IConversationStore _conversaciones;
    private readonly ReceptionAgent _agent;
    private readonly CarnetOptions _opciones;

    public ProcesadorCarnet(
        ILectorCarnet lector,
        IDpmGateway dpm,
        IConversationStore conversaciones,
        ReceptionAgent agent,
        IOptions<CarnetOptions> opciones)
    {
        _lector = lector;
        _dpm = dpm;
        _conversaciones = conversaciones;
        _agent = agent;
        _opciones = opciones.Value;
    }

    public long MaxBytes => _opciones.MaxBytes;

    /// <exception cref="EntradaRechazadaException">Imagen inválida o conversación sin teléfono.</exception>
    public async Task<CarnetReply> ProcesarAsync(byte[] datos, string? sesionId, string? telefono, CancellationToken ct)
    {
        var imagen = ImagenRecibida.Validar(datos, _opciones.MaxBytes);

        sesionId = string.IsNullOrWhiteSpace(sesionId) ? Guid.NewGuid().ToString("N")[..12] : sesionId.Trim();

        // El carnet queda atado al teléfono verificado de la conversación.
        var sesion = await _conversaciones.ObtenerAsync(sesionId, ct);
        sesion.VincularTelefono(telefono);
        if (sesion.TelefonoCanal is null)
            throw new EntradaRechazadaException("entrada.carnet_sin_telefono");

        var lectura = await _lector.LeerAsync(imagen, ct);

        CarnetLeido? carnet = null;
        string mensaje;
        if (lectura.EsCarnet)
        {
            carnet = await _dpm.GuardarCarnetPendienteAsync(sesion.Id, sesion.TelefonoCanal, sesion.PacienteId, lectura.Datos, ct);
            mensaje = PrefijoCarnet + JsonSerializer.Serialize(new
            {
                nombre = lectura.Datos.Nombre,
                ars = lectura.Datos.Ars,
                numero_afiliado = lectura.Datos.NumeroAfiliado,
            }, JsonOpts);
        }
        else
        {
            mensaje = "[Foto] La imagen recibida no parece un carnet de ARS legible.";
        }

        var reply = await _agent.RunAsync(sesion.Id, mensaje, telefono, ct);
        return new CarnetReply(reply.SesionId, carnet, reply.Respuesta, reply.Pasos, reply.Idioma);
    }
}
