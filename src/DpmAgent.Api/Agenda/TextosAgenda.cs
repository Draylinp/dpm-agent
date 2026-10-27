using DpmAgent.Api.Idiomas;

namespace DpmAgent.Api.Agenda;

/// <param name="Texto">Mensaje completo (chat web y WhatsApp dentro de la ventana de 24 horas).</param>
/// <param name="Plantilla">Clave de la plantilla de WhatsApp equivalente (ver WhatsApp:Plantillas).</param>
/// <param name="Parametros">Valores de {{1}}, {{2}}... de la plantilla, en orden.</param>
/// <param name="Idioma">Idioma del texto: es, en o ht.</param>
public sealed record MensajeAgenda(string Texto, string Plantilla, IReadOnlyList<string> Parametros, string Idioma);

/// <summary>
/// Mensajes automáticos. Son plantillas fijas en es/en/ht (Idiomas/Recursos), no salen del modelo:
/// así nunca llevan nada clínico ni inventado. Para otros idiomas se usa inglés.
/// </summary>
public static class TextosAgenda
{
    // Todas llevan el nombre del consultorio ({0} en la plantilla) y cómo darse de baja (STOP): plantillas de utilidad.
    public static MensajeAgenda Oferta(string idioma, string consultorio, string nombrePaciente, string medico, DateTimeOffset fechaHora, DateTimeOffset vence)
    {
        var i = Idiomas.Idiomas.ParaTextos(idioma);
        string[] p = [consultorio, PrimerNombre(nombrePaciente), medico, Textos.Dia(fechaHora, i), Textos.Hora(fechaHora, i), Textos.Hora(vence, i)];
        return new(Textos.Obtener("agenda.oferta", i, p), "oferta", p, i);
    }

    public static MensajeAgenda OfertaVencida(string idioma, string consultorio, DateTimeOffset fechaHora)
    {
        var i = Idiomas.Idiomas.ParaTextos(idioma);
        string[] p = [consultorio, Textos.Dia(fechaHora, i), Textos.Hora(fechaHora, i)];
        return new(Textos.Obtener("agenda.oferta_vencida", i, p), "oferta_vencida", p, i);
    }

    public static MensajeAgenda Recordatorio(string idioma, string consultorio, string nombrePaciente, string medico, DateTimeOffset fechaHora, string citaId)
    {
        var i = Idiomas.Idiomas.ParaTextos(idioma);
        string[] p = [consultorio, PrimerNombre(nombrePaciente), medico, Textos.Dia(fechaHora, i), Textos.Hora(fechaHora, i), citaId];
        return new(Textos.Obtener("agenda.recordatorio", i, p), "recordatorio", p, i);
    }

    /// <summary>Hora en español (para el panel).</summary>
    public static string Hora(DateTimeOffset d) => Textos.Hora(d, Idiomas.Idiomas.Espanol);

    private static string PrimerNombre(string nombre) => nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nombre;
}
