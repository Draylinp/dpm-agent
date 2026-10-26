using System.Globalization;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Agenda;

/// <summary>
/// Textos fijos de los mensajes automáticos. Son plantillas, no salen del modelo:
/// así nunca llevan nada clínico ni inventado.
/// </summary>
public static class TextosAgenda
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-DO");

    public static string Oferta(string nombrePaciente, string medico, DateTimeOffset fechaHora, DateTimeOffset vence) =>
        $"Hola, {PrimerNombre(nombrePaciente)}. Se liberó un turno con {medico} el {Dia(fechaHora)} a las {Hora(fechaHora)}. " +
        $"¿Lo quiere? Responda SÍ para reservarlo o NO para dejarlo pasar. La oferta vence a las {Hora(vence)}.";

    public static string OfertaVencida(DateTimeOffset fechaHora) =>
        $"El turno del {Dia(fechaHora)} a las {Hora(fechaHora)} ya no está disponible porque venció el tiempo para aceptarlo. " +
        "Sigue en la lista de espera y le avisaremos si se libera otro.";

    public static string Recordatorio(string nombrePaciente, string medico, DateTimeOffset fechaHora) =>
        $"Hola, {PrimerNombre(nombrePaciente)}. Le recordamos su cita con {medico} el {Dia(fechaHora)} a las {Hora(fechaHora)}. " +
        "Responda CONFIRMO para confirmar su asistencia o CANCELAR si no puede ir.";

    public static string Dia(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, HorarioConsultorio.ZonaRd).ToString("dddd d 'de' MMMM", Es);

    public static string Hora(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, HorarioConsultorio.ZonaRd).ToString("h:mm tt", Es);

    private static string PrimerNombre(string nombre) => nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombre;
}
