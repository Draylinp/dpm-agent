using System.Text;
using System.Text.RegularExpressions;

namespace DpmAgent.Api.Idiomas;

/// <param name="Codigo">Código BCP-47 del idioma detectado.</param>
/// <param name="Puntaje">Fuerza de la evidencia (más alto, más seguro).</param>
public sealed record IdiomaDetectado(string Codigo, double Puntaje);

/// <summary>
/// Detector de idioma determinista, sin llamadas externas: cuenta palabras y marcas propias de cada
/// idioma. Distingue kreyòl de francés por sus palabras más frecuentes (mwen, ou, pou, vle, nan...).
/// Devuelve null si el texto es ambiguo ("ok", "9:00", "no"): en ese caso se conserva el idioma
/// que ya tenía la conversación.
/// </summary>
public static partial class DetectorIdioma
{
    // Palabras casi exclusivas de un idioma: valen 3 puntos.
    private static readonly Dictionary<string, string[]> Fuertes = new()
    {
        ["es"] = ["hola", "quiero", "cita", "gracias", "mañana", "buenos", "buenas", "necesito", "doctora", "médico", "medico", "sí", "puedo", "tengo", "usted", "ustedes", "quisiera", "por favor", "también", "dónde", "cuándo", "qué", "cómo", "hijo", "hija", "señora", "quiero una", "para mañana"],
        ["en"] = ["hello", "hi", "the", "want", "appointment", "please", "thanks", "thank", "tomorrow", "yes", "need", "would", "could", "my", "what", "when", "where", "how", "doctor's", "cancel", "book", "schedule", "morning", "afternoon", "i'm", "can't", "don't"],
        ["ht"] = ["mwen", "bonjou", "bonswa", "vle", "randevou", "doktè", "demen", "mèsi", "wi", "kijan", "kounye", "konnen", "pitit", "tanpri", "ede", "anile", "pran", "jodi", "maten", "apremidi", "konfime", "fè", "genyen", "bezwen", "lopital", "paske", "kisa", "kilè", "kote", "sa a", "m ap", "map", "mwen vle"],
        ["fr"] = ["bonjour", "je", "voudrais", "rendez-vous", "merci", "oui", "demain", "médecin", "suis", "vous", "avec", "s'il", "plaît", "besoin", "j'ai", "aujourd'hui", "pourquoi", "est-ce", "annuler", "matin", "madame", "monsieur"],
        ["pt"] = ["olá", "ola", "obrigado", "obrigada", "quero", "consulta", "amanhã", "você", "voce", "sim", "não", "nao", "preciso", "médica", "marcar", "gostaria", "manhã", "bom dia", "boa tarde", "meu", "minha", "estou", "obrigado pela"],
        ["it"] = ["ciao", "buongiorno", "grazie", "vorrei", "appuntamento", "domani", "sono", "medico di", "per favore", "prenotare"],
        ["de"] = ["hallo", "guten", "danke", "ich", "möchte", "termin", "morgen", "bitte", "arzt", "ärztin", "nicht"],
    };

    // Palabras frecuentes pero compartidas a veces: valen 1 punto.
    private static readonly Dictionary<string, string[]> Comunes = new()
    {
        ["es"] = ["el", "la", "de", "que", "y", "en", "un", "una", "por", "para", "con", "mi", "me", "los", "las", "del", "es", "está", "cancelar", "médico", "lunes", "martes", "miércoles", "jueves", "viernes", "hora", "doctor"],
        ["en"] = ["i", "to", "and", "a", "you", "is", "for", "with", "on", "at", "it", "can", "have", "do", "monday", "tuesday", "wednesday", "thursday", "friday", "doctor", "time", "an"],
        ["ht"] = ["ou", "li", "nan", "pou", "ak", "yon", "se", "ki", "gen", "pa", "nou", "yo", "lè", "lendi", "madi", "mèkredi", "jedi", "vandredi", "doktè a", "di", "ka", "te", "ap", "la", "an"],
        ["fr"] = ["le", "la", "les", "de", "et", "un", "une", "pour", "est", "pas", "à", "au", "du", "des", "mon", "ma", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "docteur"],
        ["pt"] = ["o", "a", "de", "que", "e", "em", "um", "uma", "para", "com", "do", "da", "no", "na", "cancelar", "segunda", "terça", "quarta", "quinta", "sexta", "doutor", "doutora"],
        ["it"] = ["il", "di", "che", "e", "per", "con", "una", "un", "lunedì", "martedì", "dottore"],
        ["de"] = ["der", "die", "das", "und", "ein", "eine", "mit", "für", "am", "zu", "montag", "dienstag"],
    };

    // Letras que delatan un idioma.
    private static readonly (string Idioma, string Letras, double Peso)[] Marcas =
    [
        ("es", "ñ¿¡", 3),
        ("pt", "ãõ", 3),
        ("de", "ßäöü", 2),
        ("ht", "ò", 1),
    ];

    public static IdiomaDetectado? Detectar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        // Los ids del sistema (CITA-12, PAC-001...) no dicen nada del idioma del paciente.
        var minusculas = IdsDelSistema().Replace(texto, " ").ToLowerInvariant();
        var palabras = Palabras(minusculas);
        if (palabras.Count == 0)
            return null;

        var bigramas = palabras.Zip(palabras.Skip(1), (a, b) => a + " " + b).ToHashSet();
        var puntajes = new Dictionary<string, double>();

        void Sumar(string idioma, double puntos) => puntajes[idioma] = puntajes.GetValueOrDefault(idioma) + puntos;

        foreach (var (idioma, lista) in Fuertes)
        {
            foreach (var p in lista)
            {
                if (p.Contains(' ') ? bigramas.Contains(p) : palabras.Contains(p))
                    Sumar(idioma, 3);
            }
        }

        foreach (var (idioma, lista) in Comunes)
        {
            foreach (var p in lista)
            {
                if (p.Contains(' ') ? bigramas.Contains(p) : palabras.Contains(p))
                    Sumar(idioma, 1);
            }
        }

        foreach (var (idioma, letras, peso) in Marcas)
        {
            if (minusculas.IndexOfAny(letras.ToCharArray()) >= 0)
                Sumar(idioma, peso);
        }

        if (puntajes.Count == 0)
            return null;

        var orden = puntajes.OrderByDescending(p => p.Value).ToList();
        var mejor = orden[0];
        var segundo = orden.Count > 1 ? orden[1].Value : 0;

        // Hace falta evidencia suficiente y una ventaja clara sobre el segundo.
        if (mejor.Value < 2.5 || mejor.Value < segundo * 1.4)
            return null;

        return new IdiomaDetectado(mejor.Key, mejor.Value);
    }

    [GeneratedRegex(@"\b(CITA|PAC|MED|ESC|CAR|AUT|LE|OF)-\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdsDelSistema();

    private static List<string> Palabras(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
            sb.Append(char.IsLetter(c) || c is '\'' or '-' ? c : c == '’' ? '\'' : ' ');

        return sb.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim('-', '\''))
            .Where(p => p.Length > 0)
            .ToList();
    }
}
