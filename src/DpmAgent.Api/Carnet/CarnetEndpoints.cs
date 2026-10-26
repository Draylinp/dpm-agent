using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using System.Security.Claims;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using DpmAgent.Api.Voz;

namespace DpmAgent.Api.Carnet;

public sealed record CarnetReply(string SesionId, CarnetLeido? Carnet, string Respuesta, IReadOnlyList<AgentStep> Pasos);

/// <summary>Corrección de la secretaria antes de confirmar. Los campos vacíos se dejan como se leyeron.</summary>
public sealed record CorreccionCarnet(string? Nombre, string? Ars, string? NumeroAfiliado);

public static class CarnetEndpoints
{
    public static IEndpointRouteBuilder MapCarnet(this IEndpointRouteBuilder app)
    {
        // Paciente (o el panel) envía la foto: se lee, queda pendiente y el agente le pide confirmación.
        app.MapPost("/api/pacientes/carnet", async (HttpRequest http, ProcesadorCarnet procesador, CancellationToken ct) =>
        {
            var archivo = await ArchivoSubido.LeerAsync(http, "imagen", procesador.MaxBytes, "La imagen", ct);
            if (archivo.Error is not null)
                return archivo.Error;

            return await RespuestasAgente.EjecutarAsync(async () => Results.Ok(await procesador.ProcesarAsync(
                archivo.Datos!, http.Form()["sesionId"].ToString(), http.Form()["telefono"].ToString(), ct)));
        });

        // Acciones de la secretaria: requieren sesión en el panel.
        var secretaria = app.MapGroup("/api/carnets").RequireAuthorization(PanelExtensions.Politica);

        secretaria.MapGet("", async (IDpmGateway dpm, CancellationToken ct) =>
            Results.Ok(await dpm.ListarCarnetsPendientesAsync(ct)));

        secretaria.MapPost("/{id}/confirmar", async (
            string id, CorreccionCarnet? correccion, ClaimsPrincipal user, IDpmGateway dpm, Bitacora bitacora, CancellationToken ct) =>
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
                await bitacora.RegistrarAsync(user.Actor(), "carnet_confirmado", "carnet", carnet.Id,
                    $"para {r.Paciente.Id}{(datos is null ? "" : ", con correcciones")}", null, ct);
                return Results.Ok(r);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        secretaria.MapPost("/{id}/descartar", async (string id, ClaimsPrincipal user, IDpmGateway dpm, Bitacora bitacora, CancellationToken ct) =>
        {
            var carnet = await dpm.ObtenerCarnetAsync(id, ct);
            if (carnet is null)
                return Results.NotFound(new { error = $"No existe el carnet {id}." });

            try
            {
                var r = await new ServicioCarnet(dpm).DescartarAsync(carnet, ServicioCarnet.PorSecretaria, ct);
                await bitacora.RegistrarAsync(user.Actor(), "carnet_descartado", "carnet", carnet.Id, null, null, ct);
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
