using System.Text.Encodings.Web;
using System.Text.Json;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Carnet;

public sealed record CarnetReply(string SesionId, CarnetLeido? Carnet, string Respuesta, IReadOnlyList<AgentStep> Pasos);

/// <summary>Corrección de la secretaria antes de confirmar. Los campos vacíos se dejan como se leyeron.</summary>
public sealed record CorreccionCarnet(string? Nombre, string? Ars, string? NumeroAfiliado);

public static class CarnetEndpoints
{
    public const string PrefijoCarnet = "[Foto de carnet] ";

    private static readonly JsonSerializerOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IEndpointRouteBuilder MapCarnet(this IEndpointRouteBuilder app)
    {
        // Paciente (o el panel) envía la foto: se lee, queda pendiente y el agente le pide confirmación.
        app.MapPost("/api/pacientes/carnet", async (
            HttpRequest http,
            ILectorCarnet lector,
            IDpmGateway dpm,
            IConversationStore conversaciones,
            ReceptionAgent agent,
            IOptions<CarnetOptions> opciones,
            CancellationToken ct) =>
        {
            var maxBytes = opciones.Value.MaxBytes;
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "Envíe la foto como multipart/form-data en el campo 'imagen'." });

            var maxCuerpo = maxBytes + 64 * 1024;
            if (http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limite)
                limite.MaxRequestBodySize = maxCuerpo;

            IFormCollection form;
            try
            {
                form = await http.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = maxCuerpo }, ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
            {
                return Results.Problem("La imagen es demasiado grande.", statusCode: StatusCodes.Status413PayloadTooLarge);
            }

            var archivo = form.Files["imagen"];
            if (archivo is null)
                return Results.BadRequest(new { error = "Falta la foto en el campo 'imagen'." });
            if (archivo.Length > maxBytes)
                return Results.Problem("La imagen es demasiado grande.", statusCode: StatusCodes.Status413PayloadTooLarge);

            ImagenRecibida imagen;
            try
            {
                using var buffer = new MemoryStream((int)archivo.Length);
                await archivo.CopyToAsync(buffer, ct);
                imagen = ImagenRecibida.Validar(buffer.ToArray(), maxBytes);
            }
            catch (ImagenInvalidaException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var sesionId = form["sesionId"].ToString();
            sesionId = string.IsNullOrWhiteSpace(sesionId) ? Guid.NewGuid().ToString("N")[..12] : sesionId.Trim();
            var telefono = form["telefono"].ToString();

            return await RespuestasAgente.EjecutarAsync(async () =>
            {
                // El carnet queda atado al teléfono verificado de la conversación.
                var sesion = await conversaciones.ObtenerAsync(sesionId, ct);
                sesion.VincularTelefono(telefono);
                if (sesion.TelefonoCanal is null)
                    return Results.BadRequest(new { error = "Se necesita el teléfono del paciente para asociar el carnet." });

                var lectura = await lector.LeerAsync(imagen, ct);

                CarnetLeido? carnet = null;
                string mensaje;
                if (lectura.EsCarnet)
                {
                    carnet = await dpm.GuardarCarnetPendienteAsync(sesion.Id, sesion.TelefonoCanal, sesion.PacienteId, lectura.Datos, ct);
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

                var reply = await agent.RunAsync(sesion.Id, mensaje, telefono, ct);
                return Results.Ok(new CarnetReply(reply.SesionId, carnet, reply.Respuesta, reply.Pasos));
            });
        });

        // Acciones de la secretaria. Quedan detrás de la autenticación del panel en F6.
        app.MapGet("/api/carnets", async (IDpmGateway dpm, CancellationToken ct) =>
            Results.Ok(await dpm.ListarCarnetsPendientesAsync(ct)));

        app.MapPost("/api/carnets/{id}/confirmar", async (
            string id, CorreccionCarnet? correccion, IDpmGateway dpm, Bitacora bitacora, CancellationToken ct) =>
        {
            var carnet = await dpm.ObtenerCarnetAsync(id, ct);
            if (carnet is null)
                return Results.NotFound(new { error = $"No existe el carnet {id}." });

            DatosCarnet? datos = null;
            if (correccion is not null)
            {
                datos = DatosCarnet.Limpiar(correccion.Nombre, correccion.Ars, correccion.NumeroAfiliado);
                if (Rechazado(correccion.Nombre, datos.Nombre) || Rechazado(correccion.Ars, datos.Ars) ||
                    Rechazado(correccion.NumeroAfiliado, datos.NumeroAfiliado))
                {
                    return Results.BadRequest(new { error = "Algún campo corregido tiene caracteres no permitidos o es demasiado largo." });
                }
            }

            try
            {
                var r = await new ServicioCarnet(dpm).ConfirmarAsync(
                    carnet, datos, ServicioCarnet.PorSecretaria, null, exigirMismoNombre: false, ct);
                await bitacora.RegistrarAsync(ServicioCarnet.PorSecretaria, "carnet_confirmado", "carnet", carnet.Id,
                    $"para {r.Paciente.Id}{(datos is null ? "" : ", con correcciones")}", null, ct);
                return Results.Ok(r);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/carnets/{id}/descartar", async (string id, IDpmGateway dpm, Bitacora bitacora, CancellationToken ct) =>
        {
            var carnet = await dpm.ObtenerCarnetAsync(id, ct);
            if (carnet is null)
                return Results.NotFound(new { error = $"No existe el carnet {id}." });

            try
            {
                var r = await new ServicioCarnet(dpm).DescartarAsync(carnet, ServicioCarnet.PorSecretaria, ct);
                await bitacora.RegistrarAsync(ServicioCarnet.PorSecretaria, "carnet_descartado", "carnet", carnet.Id, null, null, ct);
                return Results.Ok(r);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        return app;
    }

    private static bool Rechazado(string? enviado, string? limpio) => !string.IsNullOrWhiteSpace(enviado) && limpio is null;
}
