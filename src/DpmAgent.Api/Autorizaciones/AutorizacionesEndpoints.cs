using System.Security.Claims;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Panel;

namespace DpmAgent.Api.Autorizaciones;

public sealed record NuevaAutorizacionRequest(string CitaId);

public sealed record ContenidoAutorizacionRequest(string? Contenido);

public static class AutorizacionesEndpoints
{
    public static IEndpointRouteBuilder MapAutorizaciones(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/panel/autorizaciones").RequireAuthorization(PanelExtensions.Politica);

        grupo.MapGet("", async (ServicioAutorizaciones s, CancellationToken ct) => Results.Ok(await s.PendientesAsync(ct)));

        grupo.MapPost("", (NuevaAutorizacionRequest req, ClaimsPrincipal user, ServicioAutorizaciones s, Bitacora b, CancellationToken ct) =>
            Ejecutar(async () =>
            {
                var a = await s.CrearAsync(req.CitaId, user.Identity!.Name!, ct);
                await b.RegistrarAsync(user.Actor(), "autorizacion_borrador", "autorizacion", a.Id, a.CitaId, null, ct);
                return a;
            }));

        grupo.MapPut("/{id}", (string id, ContenidoAutorizacionRequest req, ClaimsPrincipal user, ServicioAutorizaciones s, Bitacora b, CancellationToken ct) =>
            Ejecutar(async () =>
            {
                var a = await s.EditarAsync(id, req.Contenido ?? "", ct);
                await b.RegistrarAsync(user.Actor(), "autorizacion_editada", "autorizacion", a.Id, null, null, ct);
                return a;
            }));

        grupo.MapPost("/{id}/aprobar", (string id, ContenidoAutorizacionRequest? req, ClaimsPrincipal user, ServicioAutorizaciones s, Bitacora b, CancellationToken ct) =>
            Ejecutar(async () =>
            {
                var a = await s.AprobarAsync(id, req?.Contenido, user.Identity!.Name!, ct);
                await b.RegistrarAsync(user.Actor(), "autorizacion_aprobada", "autorizacion", a.Id, a.CitaId, null, ct);
                return a;
            }));

        grupo.MapPost("/{id}/descartar", (string id, ClaimsPrincipal user, ServicioAutorizaciones s, Bitacora b, CancellationToken ct) =>
            Ejecutar(async () =>
            {
                var a = await s.DescartarAsync(id, ct);
                await b.RegistrarAsync(user.Actor(), "autorizacion_descartada", "autorizacion", a.Id, a.CitaId, null, ct);
                return a;
            }));

        return app;
    }

    private static async Task<IResult> Ejecutar(Func<Task<BorradorAutorizacion>> accion)
    {
        try
        {
            return Results.Ok(await accion());
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("No existe", StringComparison.Ordinal))
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
