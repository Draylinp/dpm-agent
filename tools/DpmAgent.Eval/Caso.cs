using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DpmAgent.Eval;

/// <summary>
/// Un caso del banco de evaluación: lo que dice el paciente y lo que tiene que pasar.
/// El guion solo se usa con el modelo falso: es lo que "respondería" el modelo, a veces a propósito
/// un modelo que obedece una inyección, para comprobar que el servidor la frena.
/// </summary>
public sealed class Caso
{
    public required string Id { get; init; }

    public required string Categoria { get; init; }

    /// <summary>Idioma del paciente (BCP-47).</summary>
    public required string Idioma { get; init; }

    public string? Descripcion { get; init; }

    /// <summary>Teléfono del canal; por defecto el de María (ficticia).</summary>
    public string Telefono { get; init; } = "8095550101";

    public Preparacion? Preparacion { get; init; }

    public required List<Turno> Turnos { get; init; }

    public List<PasoGuion> Guion { get; init; } = [];

    public required Esperado Esperado { get; init; }

    [JsonIgnore]
    public int Nivel => Api.Idiomas.Idiomas.Nivel(Idioma);
}

/// <summary>Un mensaje del paciente: texto, nota de voz (texto transcrito) o foto del carnet (datos leídos).</summary>
[JsonConverter(typeof(TurnoConverter))]
public sealed class Turno
{
    public string? Texto { get; init; }

    public string? Voz { get; init; }

    public FotoCarnet? Foto { get; init; }
}

public sealed record FotoCarnet(string? Nombre, string? Ars, string? Afiliado, bool EsCarnet = true);

public sealed class Preparacion
{
    public List<CitaPreparada> Citas { get; init; } = [];

    public List<EsperaPreparada> ListaEspera { get; init; } = [];

    /// <summary>Índices de <see cref="Citas"/> que se cancelan antes de empezar; luego corre el motor de agenda.</summary>
    public List<int> Cancelar { get; init; } = [];

    public string? TelefonoEmergencias { get; init; }

    /// <summary>Idioma guardado de cada paciente (PAC-001 → en), para los mensajes automáticos.</summary>
    public Dictionary<string, string> IdiomasPacientes { get; init; } = [];

    /// <summary>Borra los permisos de avisos de la semilla (paciente que nunca dio su consentimiento).</summary>
    public bool SinConsentimientos { get; init; }
}

public sealed record CitaPreparada(string Paciente, string Medico, string Fecha, string Hora);

public sealed record EsperaPreparada(string Paciente, string Medico, string Desde, string Hasta, string HoraDesde, string HoraHasta);

/// <summary>Una respuesta del modelo falso: texto o llamadas a herramientas.</summary>
public sealed class PasoGuion
{
    public string? Texto { get; init; }

    public List<LlamadaGuion>? Herramientas { get; init; }
}

public sealed record LlamadaGuion(string Nombre, JsonObject? Args);

/// <summary>Lo que se comprueba al final. Todo campo nulo no se comprueba.</summary>
public sealed class Esperado
{
    public List<string>? Herramientas { get; init; }

    public List<string>? HerramientasProhibidas { get; init; }

    /// <summary>Citas confirmadas del paciente del teléfono del caso.</summary>
    public int? CitasPaciente { get; init; }

    /// <summary>Citas confirmadas por paciente (PAC-001 → 1).</summary>
    public Dictionary<string, int>? CitasDe { get; init; }

    /// <summary>Debe existir una cita confirmada de ese paciente a esa hora ("2026-10-27T11:00", hora de RD).</summary>
    public CitaEsperada? CitaEn { get; init; }

    public bool? Escalado { get; init; }

    public bool? Urgente { get; init; }

    /// <summary>Expresiones regulares que no pueden aparecer en ninguna respuesta (consejo clínico, dosis...).</summary>
    public List<string>? NoDebeContener { get; init; }

    /// <summary>Textos que deben aparecer en alguna respuesta.</summary>
    public List<string>? DebeContener { get; init; }

    /// <summary>Idioma de la sesión al terminar.</summary>
    public string? IdiomaSesion { get; init; }

    /// <summary>Idioma de las respuestas (detectado sobre todas juntas).</summary>
    public string? IdiomaRespuesta { get; init; }

    public int? ListaEspera { get; init; }

    public SeguroEsperado? Seguro { get; init; }

    public int? CarnetsPendientes { get; init; }

    /// <summary>Debe existir un paciente con este teléfono.</summary>
    public string? PacienteConTelefono { get; init; }

    /// <summary>Permisos de avisos vigentes del paciente del caso, por categoría (recordatorios, seguimientos, lista_espera).</summary>
    public Dictionary<string, bool>? Avisos { get; init; }

    /// <summary>Avisos que no salieron por falta de permiso y quedaron para que la secretaria llame.</summary>
    public int? AvisosParaLlamar { get; init; }

    /// <summary>Avisos automáticos (ofertas, recordatorios) que sí se encolaron para enviar.</summary>
    public int? AvisosEnviados { get; init; }

    /// <summary>Respuestas que el filtro de salida debe bloquear.</summary>
    public int? Bloqueos { get; init; }

    /// <summary>El resultado de esa herramienta nunca puede contener ese texto (por ejemplo, datos de otro paciente).</summary>
    public List<ResultadoProhibido>? ResultadoNoContiene { get; init; }
}

public sealed record CitaEsperada(string Paciente, string FechaHora);

public sealed record SeguroEsperado(string Paciente, string? Ars, string? Afiliado);

public sealed record ResultadoProhibido(string Herramienta, string Texto);

/// <summary>Un turno puede escribirse como texto simple o como objeto { "voz": ... } / { "foto": ... }.</summary>
public sealed class TurnoConverter : JsonConverter<Turno>
{
    public override Turno Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new Turno { Texto = reader.GetString() };

        var nodo = JsonNode.Parse(ref reader)!.AsObject();
        return new Turno
        {
            Texto = nodo["texto"]?.GetValue<string>(),
            Voz = nodo["voz"]?.GetValue<string>(),
            Foto = nodo["foto"]?.Deserialize<FotoCarnet>(options),
        };
    }

    public override void Write(Utf8JsonWriter writer, Turno value, JsonSerializerOptions options)
    {
        if (value.Voz is null && value.Foto is null)
        {
            writer.WriteStringValue(value.Texto);
            return;
        }

        writer.WriteStartObject();
        if (value.Voz is not null)
            writer.WriteString("voz", value.Voz);
        if (value.Foto is not null)
        {
            writer.WritePropertyName("foto");
            JsonSerializer.Serialize(writer, value.Foto, options);
        }
        writer.WriteEndObject();
    }
}

public static class Casos
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Lee todos los casos de los archivos *.json de la carpeta (cada archivo, un arreglo).</summary>
    public static List<Caso> Cargar(string carpeta)
    {
        var casos = new List<Caso>();
        foreach (var archivo in Directory.GetFiles(carpeta, "*.json").Order())
        {
            var lista = JsonSerializer.Deserialize<List<Caso>>(File.ReadAllText(archivo), Json)
                        ?? throw new InvalidDataException($"{archivo} está vacío.");
            casos.AddRange(lista);
        }

        var repetidos = casos.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Count > 0)
            throw new InvalidDataException($"Ids de caso repetidos: {string.Join(", ", repetidos)}");
        return casos;
    }

    /// <summary>Busca la carpeta eval/ subiendo desde el directorio actual.</summary>
    public static string CarpetaEval()
    {
        foreach (var inicio in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(inicio); dir is not null; dir = dir.Parent)
            {
                var eval = Path.Combine(dir.FullName, "eval");
                if (Directory.Exists(Path.Combine(eval, "casos")))
                    return eval;
            }
        }

        throw new DirectoryNotFoundException("No se encontró la carpeta eval/casos.");
    }
}
