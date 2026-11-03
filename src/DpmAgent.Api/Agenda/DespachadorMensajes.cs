using System.Text.Json;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Agenda;

/// <summary>
/// Entrega los mensajes salientes pendientes de todos los consultorios por su canal,
/// con reintentos. Un fallo de un mensaje no detiene a los demás.
/// </summary>
public sealed class DespachadorMensajes
{
    private const int Lote = 50;

    private readonly DpmDbContext _db;
    private readonly IEnumerable<IMessageChannel> _canales;
    private readonly AgendaOptions _opciones;
    private readonly TimeProvider _clock;
    private readonly ILogger<DespachadorMensajes> _logger;
    private readonly ContactoOptions _contacto;

    public DespachadorMensajes(
        DpmDbContext db,
        IEnumerable<IMessageChannel> canales,
        IOptions<AgendaOptions> opciones,
        TimeProvider clock,
        ILogger<DespachadorMensajes> logger,
        IOptions<ContactoOptions>? contacto = null)
    {
        _contacto = contacto?.Value ?? new ContactoOptions();
        _db = db;
        _canales = canales;
        _opciones = opciones.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <returns>Cuántos mensajes se entregaron.</returns>
    public async Task<int> DespacharPendientesAsync(CancellationToken ct)
    {
        var pendientes = await _db.MensajesSalientes
            .IgnoreQueryFilters()
            .Where(m => m.Estado == "pendiente")
            .OrderBy(m => m.Id)
            .Take(Lote)
            .ToListAsync(ct);

        var remitentes = pendientes.Count == 0
            ? new Dictionary<int, string?>()
            : await _db.Consultorios.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.WhatsAppPhoneNumberId, ct);

        var entregados = 0;
        foreach (var m in pendientes)
        {
            var canal = _canales.FirstOrDefault(c => c.Canal == m.Canal);
            try
            {
                if (canal is null)
                    throw new InvalidOperationException($"No hay canal configurado para '{m.Canal}'.");

                var parametros = m.ParametrosJson is null ? null : JsonSerializer.Deserialize<string[]>(m.ParametrosJson);
                await canal.EnviarAsync(new MensajeParaEnviar(
                    m.Id, m.SesionId, m.Destino, m.Texto, m.Tipo,
                    remitentes.GetValueOrDefault(m.ConsultorioId), m.Plantilla, parametros, m.Idioma), ct);
                m.Estado = "enviado";
                m.EnviadoUtc = _clock.GetUtcNow().UtcDateTime;
                m.Error = null;
                entregados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                m.Intentos++;
                m.Error = Seguridad.Privacidad.Enmascarar(ex.Message, 500);
                if (m.Intentos >= _opciones.MaxIntentosEnvio)
                    m.Estado = "fallido";
                _logger.LogWarning("No se pudo entregar el mensaje {Id} por {Canal} (intento {Intento}): {Error}",
                    m.Id, m.Canal, m.Intentos, Seguridad.Privacidad.Enmascarar(ex.Message));
            }

            await _db.SaveChangesAsync(ct);
            if (m.Estado == "fallido" &&
                await ServicioContacto.RegistrarFalloAsync(_db, m.ConsultorioId, m.Destino, _contacto.FallosParaNoLocalizable, _clock.GetUtcNow().UtcDateTime, ct))
                _logger.LogWarning("Un número del consultorio {Consultorio} quedó como no localizable.", m.ConsultorioId);
        }

        return entregados;
    }
}
