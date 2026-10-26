using System.Globalization;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Agenda;

/// <param name="Texto">Mensaje completo (chat web y WhatsApp dentro de la ventana de 24 horas).</param>
/// <param name="Plantilla">Clave de la plantilla de WhatsApp equivalente (ver WhatsApp:Plantillas).</param>
/// <param name="Parametros">Valores de {{1}}, {{2}}... de la plantilla, en orden.</param>
public sealed record MensajeAgenda(string Texto, string Plantilla, IReadOnlyList<string> Parametros);

/// <summary>
/// Textos fijos de los mensajes automáticos. Son plantillas, no salen del modelo:
/// así nunca llevan nada clínico ni inventado.
/// </summary>
public static class TextosAgenda
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-DO");

    public static MensajeAgenda Oferta(string nombrePaciente, string medico, DateTimeOffset fechaHora, DateTimeOffset vence)
    {
        string[] p = [PrimerNombre(nombrePaciente), medico, Dia(fechaHora), Hora(fechaHora), Hora(vence)];
        return new(
            $"Hola, {p[0]}. Se liberó un turno con {p[1]} el {p[2]} a las {p[3]}. " +
            $"¿Lo quiere? Responda SÍ para reservarlo o NO para dejarlo pasar. La oferta vence a las {p[4]}.",
            "oferta",
            p);
    }

    public static MensajeAgenda OfertaVencida(DateTimeOffset fechaHora)
    {
        string[] p = [Dia(fechaHora), Hora(fechaHora)];
        return new(
            $"El turno del {p[0]} a las {p[1]} ya no está disponible porque venció el tiempo para aceptarlo. " +
            "Sigue en la lista de espera y le avisaremos si se libera otro.",
            "oferta_vencida",
            p);
    }

    public static MensajeAgenda Recordatorio(string nombrePaciente, string medico, DateTimeOffset fechaHora, string citaId)
    {
        string[] p = [PrimerNombre(nombrePaciente), medico, Dia(fechaHora), Hora(fechaHora), citaId];
        return new(
            $"Hola, {p[0]}. Le recordamos su cita con {p[1]} el {p[2]} a las {p[3]} (cita {p[4]}). " +
            "Responda CONFIRMO para confirmar su asistencia o CANCELAR si no puede ir.",
            "recordatorio",
            p);
    }

    public static string Dia(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, HorarioConsultorio.ZonaRd).ToString("dddd d 'de' MMMM", Es);

    public static string Hora(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, HorarioConsultorio.ZonaRd).ToString("h:mm tt", Es);

    private static string PrimerNombre(string nombre) => nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombre;
}
