namespace DpmAgent.Api.Seguridad;

/// <summary>Cabeceras de protección del navegador para el chat, la demo, el panel y la API.</summary>
public static class EncabezadosSeguridad
{
    /// <summary>
    /// Sin 'unsafe-inline': todos los scripts y estilos están en archivos propios. Nada se puede incrustar en otra
    /// página, ni cargar objetos o cambiar la URL base, y los formularios solo se envían al mismo sitio.
    /// </summary>
    public const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; " +
        "connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    private const string Ninguno = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), payment=(), usb=()";

    /// <summary>Páginas donde se graban notas de voz (el chat y la demo, que es el mismo chat).</summary>
    private static readonly string[] PaginasConMicrofono = ["/", "/index.html"];

    public static string PermisosPara(PathString ruta) =>
        PaginasConMicrofono.Contains(ruta.Value ?? "/", StringComparer.OrdinalIgnoreCase)
            ? Ninguno + ", microphone=(self)"
            : Ninguno + ", microphone=()";
}

/// <summary>
/// Defensa CSRF adicional a la cookie SameSite=Strict: en el panel, toda petición que cambia datos (POST, PUT, PATCH,
/// DELETE) debe traer un Origin (o, si falta, un Referer) del mismo sitio. Si no coincide o no viene, se rechaza (403).
/// </summary>
public sealed class ProteccionCsrf(RequestDelegate siguiente)
{
    private static readonly string[] RutasProtegidas = ["/api/panel", "/api/bitacora", "/api/carnets", "/api/auth"];

    public async Task InvokeAsync(HttpContext contexto)
    {
        var peticion = contexto.Request;
        if (!HttpMethods.IsGet(peticion.Method) && !HttpMethods.IsHead(peticion.Method) && !HttpMethods.IsOptions(peticion.Method)
            && RutasProtegidas.Any(r => peticion.Path.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase))
            && !MismoOrigen(peticion))
        {
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            await contexto.Response.WriteAsJsonAsync(new { error = "Origen no permitido." });
            return;
        }

        await siguiente(contexto);
    }

    internal static bool MismoOrigen(HttpRequest peticion)
    {
        var origen = peticion.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origen) || origen == "null")
            origen = peticion.Headers.Referer.ToString();
        if (!Uri.TryCreate(origen, UriKind.Absolute, out var uri))
            return false;

        var propio = $"{peticion.Scheme}://{peticion.Host.Value}";
        return Uri.TryCreate(propio, UriKind.Absolute, out var host)
               && string.Equals(uri.Scheme, host.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase)
               && uri.Port == host.Port;
    }
}
