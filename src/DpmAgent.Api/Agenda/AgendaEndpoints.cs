using DpmAgent.Api.Canales;
using DpmAgent.Api.Data;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Agenda;

public sealed record MensajeSalienteDto(long Id, string Tipo, string Texto, DateTimeOffset Creado);

public static class AgendaEndpoints
{
    public static IEndpointRouteBuilder MapAgenda(this IEndpointRouteBuilder app)
    {
        // El chat web consulta aquí los mensajes que el sistema le envió por iniciativa propia.
        app.MapGet("/api/chat/{sesionId}/salientes", async (string sesionId, long? despuesDe, DpmDbContext db, CancellationToken ct) =>
        {
            var desde = despuesDe ?? 0;
            var mensajes = await db.MensajesSalientes.AsNoTracking()
                .Where(m => m.SesionId == sesionId && m.Canal == CanalesConocidos.Web && m.Estado == "enviado" && m.Id > desde)
                .OrderBy(m => m.Id)
                .Take(50)
                .ToListAsync(ct);

            return Results.Ok(mensajes.Select(m =>
                new MensajeSalienteDto(m.Id, m.Tipo, m.Texto, new DateTimeOffset(m.CreadoUtc, TimeSpan.Zero))));
        });

        app.MapGet("/api/bitacora", async (int? limite, Bitacora bitacora, CancellationToken ct) =>
            Results.Ok(await bitacora.ListarAsync(limite ?? 100, ct))).RequireAuthorization(PanelExtensions.Politica);

        app.MapGet("/api/lista-espera", async (ServicioListaEspera lista, CancellationToken ct) =>
            Results.Ok(await lista.ListarActivasAsync(ct))).RequireAuthorization(PanelExtensions.Politica);

        return app;
    }
}
