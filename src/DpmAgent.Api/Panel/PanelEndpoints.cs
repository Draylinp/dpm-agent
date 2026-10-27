using System.Globalization;
using System.Security.Claims;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DpmAgent.Api.Panel;

public sealed record LoginRequest(string Usuario, string Clave);

public sealed record NuevoUsuarioRequest(string Usuario, string Nombre, string Clave, string Rol);

/// <param name="IdiomaPredeterminado">Idioma del primer saludo: es o en.</param>
/// <param name="TelefonoEmergencias">Número que el agente indica ante una posible emergencia.</param>
public sealed record ConfiguracionConsultorio(string? IdiomaPredeterminado, string? TelefonoEmergencias, string? NombreAsistente = null);

public static class PanelEndpoints
{
    public static IEndpointRouteBuilder MapPanel(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest req, HttpContext http, ServicioUsuarios usuarios, Bitacora bitacora, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Usuario) || string.IsNullOrEmpty(req.Clave) || req.Clave.Length > 200)
                return Results.Json(new { error = "Usuario o clave incorrectos." }, statusCode: StatusCodes.Status401Unauthorized);

            var u = await usuarios.ValidarAsync(req.Usuario, req.Clave, ct);
            if (u is null)
                return Results.Json(new { error = "Usuario o clave incorrectos." }, statusCode: StatusCodes.Status401Unauthorized);

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                ServicioUsuarios.APrincipal(u, CookieAuthenticationDefaults.AuthenticationScheme));
            await bitacora.RegistrarEnConsultorioAsync(u.ConsultorioId, $"secretaria:{u.Usuario}", "inicio_sesion", ct);
            return Results.Ok(new { u.Usuario, u.Nombre, u.Rol });
        }).RequireRateLimiting(ProduccionExtensions.PoliticaLogin);

        app.MapPost("/api/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        app.MapGet("/api/auth/yo", (ClaimsPrincipal user) => Results.Ok(new
        {
            usuario = user.Identity!.Name,
            nombre = user.FindFirstValue("nombre"),
            rol = user.FindFirstValue(ClaimTypes.Role),
        })).RequireAuthorization(PanelExtensions.Politica);

        var panel = app.MapGroup("/api/panel").RequireAuthorization(PanelExtensions.Politica);

        panel.MapGet("/citas", async (string? fecha, ServicioPanel servicio, TimeProvider clock, CancellationToken ct) =>
        {
            var dia = Dpm.HorarioConsultorio.FechaRd(clock.GetUtcNow());
            if (!string.IsNullOrWhiteSpace(fecha) &&
                !DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dia))
            {
                return Results.BadRequest(new { error = "La fecha debe tener formato YYYY-MM-DD." });
            }

            return Results.Ok(await servicio.CitasDelDiaAsync(dia, ct));
        });

        panel.MapGet("/escalamientos", async (bool? todos, ServicioPanel servicio, CancellationToken ct) =>
            Results.Ok(await servicio.EscalamientosAsync(soloPendientes: todos != true, ct)));

        panel.MapPost("/escalamientos/{id}/atender", async (
            string id, ClaimsPrincipal user, ServicioPanel servicio, Bitacora bitacora, CancellationToken ct) =>
        {
            if (!await servicio.AtenderEscalamientoAsync(id, user.Identity!.Name!, ct))
                return Results.NotFound(new { error = $"No existe el escalamiento {id}." });

            await bitacora.RegistrarAsync(user.Actor(), "escalamiento_atendido", "escalamiento", id, null, null, ct);
            return Results.Ok();
        });

        panel.MapGet("/consultorio", async (Dpm.IDpmGateway dpm, CancellationToken ct) =>
            Results.Ok(await dpm.ObtenerConsultorioAsync(ct)));

        panel.MapPut("/consultorio", async (
            ConfiguracionConsultorio req, ClaimsPrincipal user, Data.DpmDbContext db, Bitacora bitacora, CancellationToken ct) =>
        {
            var idioma = req.IdiomaPredeterminado?.Trim().ToLowerInvariant();
            if (idioma is not null and not ("es" or "en"))
                return Results.BadRequest(new { error = "El idioma predeterminado debe ser 'es' o 'en'." });

            var telefono = req.TelefonoEmergencias?.Trim();
            if (telefono is not null && (telefono.Length is < 2 or > 15 || !telefono.All(char.IsAsciiDigit)))
                return Results.BadRequest(new { error = "El número de emergencias debe tener de 2 a 15 dígitos." });

            var asistente = req.NombreAsistente?.Trim();
            if (asistente is not null && (asistente.Length is < 2 or > 40 || !asistente.All(c => char.IsLetter(c) || c is ' ' or '-' or '.')))
                return Results.BadRequest(new { error = "El nombre del asistente debe tener de 2 a 40 letras." });

            var consultorio = await db.Consultorios.FirstAsync(c => c.Id == db.ConsultorioId, ct);
            consultorio.IdiomaPredeterminado = idioma ?? consultorio.IdiomaPredeterminado;
            consultorio.TelefonoEmergencias = telefono ?? consultorio.TelefonoEmergencias;
            consultorio.NombreAsistente = asistente ?? consultorio.NombreAsistente;
            await db.SaveChangesAsync(ct);

            await bitacora.RegistrarAsync(user.Actor(), "consultorio_configurado", "consultorio", consultorio.Id.ToString(CultureInfo.InvariantCulture),
                $"idioma={consultorio.IdiomaPredeterminado}, emergencias={consultorio.TelefonoEmergencias}, asistente={consultorio.NombreAsistente}", null, ct);
            return Results.Ok(new Dpm.InfoConsultorio(
                consultorio.Nombre, consultorio.TelefonoEmergencias, consultorio.IdiomaPredeterminado, consultorio.NombreAsistente));
        }).RequireAuthorization(PanelExtensions.PoliticaAdmin);

        panel.MapPost("/usuarios", async (
            NuevoUsuarioRequest req, ClaimsPrincipal user, ServicioUsuarios usuarios, Bitacora bitacora, CancellationToken ct) =>
        {
            var consultorio = int.Parse(user.FindFirstValue(ServicioUsuarios.ClaimConsultorio)!, CultureInfo.InvariantCulture);
            try
            {
                var u = await usuarios.CrearAsync(consultorio, req.Usuario, req.Nombre, req.Clave, req.Rol, ct);
                await bitacora.RegistrarAsync(user.Actor(), "usuario_creado", "usuario", u.Usuario, u.Rol, null, ct);
                return Results.Ok(new { u.Usuario, u.Nombre, u.Rol });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).RequireAuthorization(PanelExtensions.PoliticaAdmin);

        return app;
    }
}
