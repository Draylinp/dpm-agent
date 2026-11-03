using System.Security.Cryptography;
using System.Text;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Canales.WhatsApp;

public static class WhatsAppEndpoints
{
    private const int MaxCuerpo = 1024 * 1024;

    public static IEndpointRouteBuilder MapWhatsApp(this IEndpointRouteBuilder app)
    {
        // Meta verifica la suscripción con un GET que debe devolver hub.challenge.
        app.MapGet("/api/whatsapp/webhook", (HttpRequest http, IOptions<WhatsAppOptions> opciones) =>
        {
            var esperado = opciones.Value.VerifyToken;
            var modo = http.Query["hub.mode"].ToString();
            var token = http.Query["hub.verify_token"].ToString();
            var reto = http.Query["hub.challenge"].ToString();

            if (string.IsNullOrEmpty(esperado) || modo != "subscribe" || !IgualesEnTiempoConstante(token, esperado))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            return Results.Text(reto, "text/plain");
        });

        // Mensajes nuevos. Se valida la firma, se guardan y se responde 200 enseguida;
        // el procesador en segundo plano los pasa al agente.
        app.MapPost("/api/whatsapp/webhook", async (
            HttpRequest http,
            IOptions<WhatsAppOptions> opciones,
            BaseDatosOptions baseDatos,
            DpmDbContext db,
            DespertadorEntrantes despertador,
            TimeProvider clock,
            ILoggerFactory logs,
            CancellationToken ct) =>
        {
            var log = logs.CreateLogger("WhatsAppWebhook");
            var secreto = opciones.Value.AppSecret;
            if (string.IsNullOrWhiteSpace(secreto))
                return Results.Problem("WhatsApp no está configurado.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var cuerpo = await LeerCuerpoAsync(http, ct);
            if (cuerpo is null)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            if (!WebhookWhatsApp.FirmaValida(cuerpo, http.Headers["X-Hub-Signature-256"], secreto))
            {
                log.LogWarning("Webhook de WhatsApp con firma inválida desde {Ip}", http.HttpContext.Connection.RemoteIpAddress);
                return Results.Unauthorized();
            }

            var mensajes = WebhookWhatsApp.LeerMensajes(cuerpo);
            if (mensajes.Count == 0)
                return Results.Ok();

            var consultorios = await db.Consultorios.AsNoTracking()
                .Where(c => c.WhatsAppPhoneNumberId != null)
                .ToDictionaryAsync(c => c.WhatsAppPhoneNumberId!, c => c.Id, ct);

            var nuevos = 0;
            foreach (var m in mensajes)
            {
                int? consultorio = consultorios.TryGetValue(m.PhoneNumberId, out var id) ? id
                    : m.PhoneNumberId == opciones.Value.PhoneNumberId ? baseDatos.ConsultorioId
                    : null;
                var telefono = Telefono.Normalizar(m.Telefono);
                if (consultorio is null || telefono is null)
                {
                    log.LogWarning("Mensaje de WhatsApp para un número no configurado ({PhoneNumberId}); se ignora.", m.PhoneNumberId);
                    continue;
                }

                // Meta reintenta si no recibe 200 a tiempo: el id del mensaje evita procesarlo dos veces.
                if (await db.MensajesEntrantes.IgnoreQueryFilters().AnyAsync(e => e.ProveedorId == m.ProveedorId, ct))
                    continue;

                db.MensajesEntrantes.Add(new MensajeEntrante
                {
                    ConsultorioId = consultorio.Value,
                    ProveedorId = m.ProveedorId,
                    Telefono = telefono,
                    Tipo = m.Tipo,
                    Texto = m.Texto is { Length: > 4096 } t ? t[..4096] : m.Texto,
                    MediaId = m.MediaId,
                    Estado = "pendiente",
                    RecibidoUtc = clock.GetUtcNow().UtcDateTime,
                });

                try
                {
                    await db.SaveChangesAsync(ct);
                    nuevos++;
                }
                catch (DbUpdateException)
                {
                    // Llegó el mismo reintento en paralelo: ya está guardado.
                    db.ChangeTracker.Clear();
                }
            }

            if (nuevos > 0)
                despertador.Despertar();
            return Results.Ok();
        });

        return app;
    }

    private static async Task<byte[]?> LeerCuerpoAsync(HttpRequest http, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var bloque = new byte[16 * 1024];
        int leidos;
        while ((leidos = await http.Body.ReadAsync(bloque, ct)) > 0)
        {
            if (buffer.Length + leidos > MaxCuerpo)
                return null;
            buffer.Write(bloque, 0, leidos);
        }

        return buffer.ToArray();
    }

    private static bool IgualesEnTiempoConstante(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
