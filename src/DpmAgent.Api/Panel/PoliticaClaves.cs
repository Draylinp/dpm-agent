namespace DpmAgent.Api.Panel;

/// <summary>Requisitos mínimos de las claves del panel: al menos 12 caracteres y nada de claves comunes o adivinables.</summary>
public static class PoliticaClaves
{
    public const int MinLargo = 12;
    public const int MaxLargo = 200;

    // Claves y raíces muy usadas (en inglés y en español, y propias de un consultorio). Se comparan sin mayúsculas,
    // sin acentos y sin los números o signos del final ("Consultorio2026!" cuenta como "consultorio").
    private static readonly HashSet<string> Comunes = new(StringComparer.Ordinal)
    {
        "password", "passw0rd", "contrasena", "contrasenia", "clave", "secreto", "secret", "admin", "administrador", "administrator",
        "qwerty", "qwertyuiop", "asdfghjkl", "zxcvbnm", "abc", "abcdef", "abcdefgh", "letmein", "welcome", "bienvenido", "bienvenida",
        "iloveyou", "teamo", "monkey", "dragon", "football", "baseball", "futbol", "beisbol", "superman", "batman", "princesa", "princess",
        "sunshine", "master", "shadow", "trustno1", "changeme", "cambiame", "default", "usuario", "user", "login", "acceso", "entrar",
        "consultorio", "clinica", "hospital", "medico", "doctor", "doctora", "secretaria", "recepcion", "paciente", "pacientes", "salud",
        "dominicana", "republicadominicana", "santodomingo", "santiago", "quisqueya", "dpm", "dpmagent", "agente", "panel", "demo",
    };

    /// <returns>null si la clave sirve; si no, el motivo en español.</returns>
    public static string? Validar(string? clave, string? usuario)
    {
        if (string.IsNullOrEmpty(clave) || clave.Length < MinLargo)
            return $"La clave debe tener al menos {MinLargo} caracteres.";
        if (clave.Length > MaxLargo)
            return $"La clave puede tener como máximo {MaxLargo} caracteres.";

        var simple = Simplificar(clave);
        if (simple.Length == 0 || Comunes.Contains(simple) || Comunes.Contains(simple.TrimEnd('s')))
            return "Esa clave es demasiado común. Use una frase o una combinación difícil de adivinar.";
        if (clave.Distinct().Count() < 4)
            return "La clave repite demasiado los mismos caracteres.";
        if (EsSecuencia(clave))
            return "La clave no puede ser una secuencia (como 123456789012 o abcdefghijkl).";
        if (!string.IsNullOrWhiteSpace(usuario) && usuario.Length >= 3 && Simplificar(clave).Contains(Simplificar(usuario), StringComparison.Ordinal))
            return "La clave no puede contener el nombre de usuario.";
        return null;
    }

    /// <summary>Minúsculas, sin acentos y sin los dígitos o signos del principio y del final.</summary>
    private static string Simplificar(string texto)
    {
        var sin = Cumplimiento.PedidosDirectos.SinAcentos(texto).ToLowerInvariant();
        return sin.Trim().Trim("0123456789!@#$%^&*()_-+=.,;:?¿¡ ".ToCharArray()).Replace(" ", "");
    }

    private static bool EsSecuencia(string clave)
    {
        var c = clave.ToLowerInvariant();
        var subiendo = 0;
        var bajando = 0;
        for (var i = 1; i < c.Length; i++)
        {
            if (c[i] == c[i - 1] + 1) subiendo++;
            if (c[i] == c[i - 1] - 1) bajando++;
        }

        return subiendo >= c.Length - 2 || bajando >= c.Length - 2;
    }
}
