using System.Security.Claims;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;

namespace DpmAgent.Api.Cumplimiento;

/// <param name="Categorias">Vacío o null: todas.</param>
public sealed record ConsentimientoPresencialRequest(bool Acepta, string[]? Categorias, string? Idioma);

/// <param name="Confirmacion">Debe ser exactamente "BORRAR": el borrado no se puede deshacer.</param>
public sealed record BorrarDatosRequest(string? Confirmacion);

public sealed record LlamadaPendiente(long Id, string Tipo, string Paciente, string Telefono, string Texto, string? Idioma, DateTimeOffset Creado);

public static class CumplimientoEndpoints
{
    public static IServiceCollection AddCumplimiento(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CumplimientoOptions>(config.GetSection(CumplimientoOptions.SectionName));
        services.AddScoped<ServicioConsentimiento>();
        services.AddScoped<ServicioPrivacidad>();
        return services;
    }

    public static IEndpointRouteBuilder MapCumplimiento(this IEndpointRouteBuilder app)
    {
        var panel = app.MapGroup("/api/panel").RequireAuthorization(PanelExtensions.Politica);

        // Avisos que no se enviaron por falta de consentimiento: la secretaria llama al paciente.
        panel.MapGet("/llamadas", async (DpmDbContext db, CancellationToken ct) =>
        {
            var pendientes = await db.MensajesSalientes.AsNoTracking()
                .Where(m => m.Estado == Notificador.EstadoLlamar)
                .OrderBy(m => m.Id)
                .Take(200)
                .ToListAsync(ct);
            var telefonos = pendientes.Select(m => m.Destino).Distinct().ToList();
            var nombres = await db.Pacientes.AsNoTracking().Where(p => telefonos.Contains(p.Telefono))
                .ToDictionaryAsync(p => p.Telefono, p => p.Nombre, ct);

            return Results.Ok(pendientes.Select(m => new LlamadaPendiente(
                m.Id, m.Tipo, nombres.GetValueOrDefault(m.Destino, "?"), m.Destino, m.Texto, m.Idioma,
                new DateTimeOffset(m.CreadoUtc, TimeSpan.Zero))));
        });

        panel.MapPost("/llamadas/{id:long}/hecha", async (long id, ClaimsPrincipal user, DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            var m = await db.MensajesSalientes.FirstOrDefaultAsync(x => x.Id == id && x.Estado == Notificador.EstadoLlamar, ct);
            if (m is null)
                return Results.NotFound();
            m.Estado = Notificador.EstadoLlamado;
            await db.SaveChangesAsync(ct);
            await bitacora.RegistrarAsync(user.Actor(), "llamada_hecha", "aviso", $"MS-{m.Id}", null, m.Tipo, ct);
            return Results.Ok(new { hecha = true });
        });

        panel.MapGet("/pacientes", async (string? telefono, ClaimsPrincipal user, DpmDbContext db, ServicioConsentimiento servicio, TimeProvider clock, CancellationToken ct) =>
        {
            var normalizado = Telefono.Normalizar(telefono);
            if (normalizado is null)
                return Results.BadRequest(new { error = "Indique un teléfono válido." });
            var p = await db.Pacientes.AsNoTracking().FirstOrDefaultAsync(x => x.Telefono == normalizado, ct);
            if (p is null)
                return Results.NotFound();
            await Panel.RegistroAccesos.RegistrarAsync(db, user, Panel.RegistroAccesos.VerFicha, [p.Id], clock, ct);
            return Results.Ok(new { id = IdsDpm.Paciente(p.Id), nombre = p.Nombre, consentimientos = await servicio.EstadoAsync(p.Id, ct) });
        });

        panel.MapGet("/pacientes/{id}/consentimientos", async (string id, ServicioConsentimiento servicio, CancellationToken ct) =>
            IdsDpm.Leer(id, "PAC") is { } pid
                ? Results.Ok(await servicio.EstadoAsync(pid, ct))
                : Results.NotFound());

        // Consentimiento dado (o retirado) en persona, registrado por la secretaria.
        panel.MapPost("/pacientes/{id}/consentimientos", async (
            string id, ConsentimientoPresencialRequest req, ClaimsPrincipal user, ServicioConsentimiento servicio, DpmDbContext db, CancellationToken ct) =>
        {
            if (IdsDpm.Leer(id, "PAC") is not { } pid || !await db.Pacientes.AnyAsync(p => p.Id == pid, ct))
                return Results.NotFound();
            try
            {
                await servicio.RegistrarAsync(pid, req.Categorias is { Length: > 0 } c ? c : null, req.Acepta,
                    CanalesConsentimiento.Presencial, Idiomas.Idiomas.Normalizar(req.Idioma), user.Actor(), null, ct);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            return Results.Ok(await servicio.EstadoAsync(pid, ct));
        });

        // Derechos del paciente: copia de sus datos y borrado, a petición suya.
        panel.MapGet("/pacientes/{id}/exportar", async (string id, ClaimsPrincipal user, ServicioPrivacidad privacidad, DpmDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (IdsDpm.Leer(id, "PAC") is not { } pid || await privacidad.ExportarAsync(pid, user.Actor(), ct) is not { } datos)
                return Results.NotFound();
            await Panel.RegistroAccesos.RegistrarAsync(db, user, Panel.RegistroAccesos.Exportar, [pid], clock, ct);
            return Results.Text(datos.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }), "application/json; charset=utf-8");
        });

        panel.MapPost("/pacientes/{id}/borrar", async (
            string id, BorrarDatosRequest req, ClaimsPrincipal user, ServicioPrivacidad privacidad, DpmDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (req.Confirmacion != "BORRAR")
                return Results.BadRequest(new { error = "Para borrar escriba BORRAR: no se puede deshacer." });
            if (IdsDpm.Leer(id, "PAC") is not { } pid || await privacidad.BorrarAsync(pid, user.Actor(), ct) is not { } r)
                return Results.NotFound();
            await Panel.RegistroAccesos.RegistrarAsync(db, user, Panel.RegistroAccesos.Borrar, [pid], clock, ct);
            return Results.Ok(r);
        });

        // Texto que la secretaria lee o entrega al paciente antes de registrar el consentimiento presencial.
        panel.MapGet("/consentimiento/texto", async (string? idioma, ServicioConsentimiento servicio, IDpmGateway dpm, CancellationToken ct) =>
        {
            var consultorio = await dpm.ObtenerConsultorioAsync(ct);
            return Results.Ok(new { version = ServicioConsentimiento.VersionTexto, texto = servicio.Texto(idioma, consultorio.NombreVisible) });
        });

        return app;
    }
}
