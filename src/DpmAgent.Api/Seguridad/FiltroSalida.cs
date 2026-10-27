using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DpmAgent.Api.Model;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Seguridad;

/// <param name="Bloquear">true si la respuesta no se debe enviar al paciente.</param>
/// <param name="Motivo">Por qué (en español, para la bitácora).</param>
/// <param name="Fuente">Qué capa decidió: reglas, llama-guard o modelo.</param>
/// <param name="Categoria">clinico (consejo médico: se escala) o fuera_de_alcance (respuesta de propósito general: no se escala).</param>
public sealed record VeredictoFiltro(bool Bloquear, string Motivo, string Fuente, string Categoria = VeredictoFiltro.Clinico)
{
    public const string Clinico = "clinico";
    public const string FueraDeAlcance = "fuera_de_alcance";

    public static readonly VeredictoFiltro Permitir = new(false, "", "");
}

/// <summary>
/// Revisa cada respuesta del agente antes de enviarla: no puede contener consejo médico
/// (dosis, diagnósticos, interpretación de síntomas o resultados), en ningún idioma.
/// </summary>
public interface IFiltroSalida
{
    Task<VeredictoFiltro> RevisarAsync(string respuesta, string? mensajePaciente, CancellationToken ct);
}

/// <summary>Configuración del filtro (sección "FiltroSalida").</summary>
public sealed class FiltroSalidaOptions
{
    public const string SectionName = "FiltroSalida";

    /// <summary>
    /// Reglas (solo reglas deterministas), LlamaGuard (Llama Guard con pesos abiertos en un servidor
    /// compatible con OpenAI) o Modelo (el modelo configurado con un prompt clasificador).
    /// Las reglas corren siempre primero.
    /// </summary>
    public string Proveedor { get; set; } = "Modelo";

    public string? BaseUrl { get; set; }

    public string? Model { get; set; }

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Si el clasificador falla (caído, respuesta ilegible): false deja pasar la respuesta (las reglas ya la
    /// revisaron); true la bloquea. Bloquear todo cuando el clasificador no responde deja al paciente sin atención.
    /// </summary>
    public bool BloquearSiFalla { get; set; }
}

/// <summary>
/// Capa 0, determinista y gratuita: dosis, frecuencias y "tome/take/pran + medicamento"
/// en español, inglés, kreyòl, francés y portugués.
/// </summary>
public sealed partial class FiltroReglas : IFiltroSalida
{
    public Task<VeredictoFiltro> RevisarAsync(string respuesta, string? mensajePaciente, CancellationToken ct)
    {
        foreach (var (regla, motivo) in Reglas)
        {
            if (regla.IsMatch(respuesta))
                return Task.FromResult(new VeredictoFiltro(true, motivo, "reglas"));
        }

        // Una recepción nunca necesita código: es la señal más clara de una respuesta de propósito general.
        if (BloqueDeCodigo().IsMatch(respuesta))
            return Task.FromResult(new VeredictoFiltro(true, "La respuesta contiene código (fuera del alcance del consultorio).", "reglas", VeredictoFiltro.FueraDeAlcance));

        // Las instrucciones internas (el prompt del sistema, los nombres de las herramientas) nunca se le muestran a nadie,
        // aunque el modelo obedezca a quien se las pide.
        if (InstruccionesInternas().IsMatch(respuesta))
            return Task.FromResult(new VeredictoFiltro(true, "La respuesta muestra instrucciones internas del agente.", "reglas", VeredictoFiltro.FueraDeAlcance));

        return Task.FromResult(VeredictoFiltro.Permitir);
    }

    private static readonly (Regex Regla, string Motivo)[] Reglas =
    [
        (Dosis(), "La respuesta contiene una dosis."),
        (Frecuencia(), "La respuesta indica una frecuencia de toma."),
        (Unidades(), "La respuesta indica una cantidad de pastillas o tabletas."),
        (Indicacion(), "La respuesta indica tomar o dar un medicamento."),
    ];

    [GeneratedRegex(@"```|^\s*(def|function|class|import|public static|SELECT|#include)\b[^\n]*[({:;]", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BloqueDeCodigo();

    [GeneratedRegex(
        @"\b(buscar_paciente|seleccionar_paciente|registrar_paciente|consultar_disponibilidad|crear_cita|citas_del_paciente|cancelar_cita|"
        + @"escalar_a_personal|confirmar_datos_carnet|agregar_lista_espera|responder_oferta|confirmar_asistencia|ofrecer_consentimiento|"
        + @"registrar_consentimiento|darse_de_baja|seguimientos_del_paciente|pedir_cita_urgente|reservar_urgente|reprogramar_cita|"
        + @"confirmar_numero|crear_seguimiento|bloquear_agenda|resumen_diario|decidir_sobrecupo|avisar_retraso|mi_agenda)\b"
        + @"|Reglas obligatorias|Eres (el|la) asistente (de recepción|administrativ)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstruccionesInternas();

    [GeneratedRegex(@"\b\d+([.,]\d+)?\s*(mg|mcg|µg|ml|cc|gotas|drops|gout|ui|iu)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Dosis();

    [GeneratedRegex(@"\b(cada|every|chak|toutes les|a cada)\s+\d+\s*(horas?|hours?|hrs?|h|è|zè|heures?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Frecuencia();

    [GeneratedRegex(@"\b\d+\s*(tabletas?|pastillas?|c[aá]psulas?|comprimidos?|tablets?|pills?|capsules?|grenn|comprim[eé]s?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Unidades();

    [GeneratedRegex(
        @"\b(t[oó]me(se)?|tomar|t[oó]mela|dele|darle|d[eé]le|take|give|pran|bay|prenez|donnez|tome|d[eê])\b[^.?!\n]{0,40}\b(ibuprofen\w*|ibipwof\w*|paracetam\w*|parasetam\w*|acetamin\w*|amox\w*|amoks\w*|aspirin\w*|antibi[oó]tic\w*|antibyotik\w*|antibiotique\w*|antibi[oó]tico\w*|dipirona|metamizol)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Indicacion();
}

/// <summary>
/// Capa 1 con Llama Guard (pesos abiertos de Meta) servido por vLLM con endpoint compatible con OpenAI.
/// Se le pasa el turno (paciente + agente) y clasifica la respuesta del agente: "safe" o "unsafe" con
/// las categorías violadas. S6 ("Specialized Advice") cubre el consejo médico.
/// </summary>
public sealed class FiltroLlamaGuard : IFiltroSalida
{
    private readonly HttpClient _http;
    private readonly FiltroSalidaOptions _opciones;
    private readonly IRegistroUso? _registro;

    public FiltroLlamaGuard(HttpClient http, IOptions<FiltroSalidaOptions> opciones, IRegistroUso? registro = null)
    {
        _http = http;
        _opciones = opciones.Value;
        _registro = registro;
        if (string.IsNullOrWhiteSpace(_opciones.BaseUrl) || string.IsNullOrWhiteSpace(_opciones.Model))
            throw new InvalidOperationException("FiltroSalida con LlamaGuard necesita FiltroSalida:BaseUrl y FiltroSalida:Model.");

        _http.BaseAddress ??= new Uri(_opciones.BaseUrl.EndsWith('/') ? _opciones.BaseUrl : _opciones.BaseUrl + "/");
        _http.Timeout = TimeSpan.FromSeconds(_opciones.TimeoutSeconds);
    }

    public async Task<VeredictoFiltro> RevisarAsync(string respuesta, string? mensajePaciente, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = _opciones.Model,
            ["temperature"] = 0,
            ["max_tokens"] = 20,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "user", ["content"] = string.IsNullOrWhiteSpace(mensajePaciente) ? "(sin mensaje)" : mensajePaciente },
                new JsonObject { ["role"] = "assistant", ["content"] = respuesta }),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(_opciones.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opciones.ApiKey);

        var inicio = DateTime.UtcNow;
        using var res = await _http.SendAsync(request, ct);
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"Llama Guard respondió {(int)res.StatusCode}.", null, res.StatusCode);

        var json = JsonNode.Parse(raw)!;
        using (ContextoUso.Usar("filtro"))
        {
            _registro?.Registrar(ContextoUso.Uso("Llama Guard", _opciones.Model!,
                json["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0, json["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0,
                (int)(DateTime.UtcNow - inicio).TotalMilliseconds));
        }

        return Interpretar(json["choices"]?[0]?["message"]?["content"]?.ToString());
    }

    /// <summary>"safe" → permitir; "unsafe\nS6,S11" → bloquear con las categorías.</summary>
    internal static VeredictoFiltro Interpretar(string? salida)
    {
        var texto = salida?.Trim() ?? "";
        if (texto.StartsWith("safe", StringComparison.OrdinalIgnoreCase))
            return VeredictoFiltro.Permitir;
        if (!texto.StartsWith("unsafe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Salida de Llama Guard no reconocida: {texto}");

        var categorias = texto["unsafe".Length..].Trim();
        var motivo = categorias.Contains("S6", StringComparison.OrdinalIgnoreCase)
            ? $"Llama Guard: consejo especializado (médico) [{categorias}]."
            : $"Llama Guard: contenido no seguro [{categorias}].";
        return new VeredictoFiltro(true, motivo, "llama-guard");
    }
}

/// <summary>
/// Capa 1 sin Llama Guard: el modelo configurado con un prompt clasificador estricto y respuesta JSON.
/// </summary>
public sealed class FiltroModelo(IChatModel modelo) : IFiltroSalida
{
    private const string Instrucciones = """
        Eres un clasificador de seguridad para el asistente de recepción de un consultorio médico.
        Recibes el mensaje del paciente y la RESPUESTA del asistente, en cualquier idioma.
        Decide si la RESPUESTA contiene consejo médico: medicamentos, dosis o frecuencias; tratamientos o remedios;
        interpretación de síntomas, de resultados o de valores; diagnósticos; o decir si algo es grave o no lo es.
        NO es consejo médico: agendar, dar horarios, decir que el médico responderá, pasar el caso al personal,
        o indicar que llame al número de emergencias o vaya a emergencias.
        Responde SOLO con JSON: {"consejo_medico": true|false, "motivo": "frase corta"}
        """;

    public async Task<VeredictoFiltro> RevisarAsync(string respuesta, string? mensajePaciente, CancellationToken ct)
    {
        using var _ = ContextoUso.Usar("filtro");
        var r = await modelo.CompleteAsync(
        [
            new JsonObject { ["role"] = "system", ["content"] = Instrucciones },
            new JsonObject { ["role"] = "user", ["content"] = $"MENSAJE DEL PACIENTE:\n{mensajePaciente}\n\nRESPUESTA DEL ASISTENTE:\n{respuesta}" },
        ], [], ct);

        return Interpretar(r["content"]?.ToString());
    }

    internal static VeredictoFiltro Interpretar(string? salida)
    {
        var texto = salida ?? "";
        var inicio = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio)
            throw new InvalidOperationException("El clasificador no devolvió JSON.");

        var json = JsonNode.Parse(texto[inicio..(fin + 1)])!.AsObject();
        var consejo = json["consejo_medico"]?.GetValueKind() == JsonValueKind.True;
        return consejo
            ? new VeredictoFiltro(true, $"Clasificador: {json["motivo"]?.ToString() ?? "consejo médico"}", "modelo")
            : VeredictoFiltro.Permitir;
    }
}

/// <summary>Reglas primero (rápidas y gratis); si pasan, el clasificador configurado.</summary>
public sealed class FiltroCompuesto(
    FiltroReglas reglas, IFiltroSalida? clasificador, IOptions<FiltroSalidaOptions> opciones, ILogger<FiltroCompuesto> logger) : IFiltroSalida
{
    public async Task<VeredictoFiltro> RevisarAsync(string respuesta, string? mensajePaciente, CancellationToken ct)
    {
        var porReglas = await reglas.RevisarAsync(respuesta, mensajePaciente, ct);
        if (porReglas.Bloquear || clasificador is null)
            return porReglas;

        try
        {
            return await clasificador.RevisarAsync(respuesta, mensajePaciente, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("El clasificador de salida falló ({Motivo}); {Accion}.",
                ex.Message, opciones.Value.BloquearSiFalla ? "se bloquea la respuesta" : "se envía con la revisión de reglas");
            return opciones.Value.BloquearSiFalla
                ? new VeredictoFiltro(true, "El clasificador no respondió y la configuración exige bloquear.", "falla")
                : VeredictoFiltro.Permitir;
        }
    }
}

public static class FiltroSalidaExtensions
{
    public static IServiceCollection AddFiltroSalida(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<FiltroSalidaOptions>(config.GetSection(FiltroSalidaOptions.SectionName));
        var proveedor = (config[$"{FiltroSalidaOptions.SectionName}:Proveedor"] ?? "Modelo").Trim().ToLowerInvariant();

        services.AddSingleton<FiltroReglas>();
        switch (proveedor)
        {
            case "reglas":
                services.AddScoped<IFiltroSalida>(sp => new FiltroCompuesto(sp.GetRequiredService<FiltroReglas>(), null,
                    sp.GetRequiredService<IOptions<FiltroSalidaOptions>>(), sp.GetRequiredService<ILogger<FiltroCompuesto>>()));
                break;
            case "llamaguard":
                services.AddHttpClient<FiltroLlamaGuard>();
                services.AddScoped<IFiltroSalida>(sp => new FiltroCompuesto(sp.GetRequiredService<FiltroReglas>(), sp.GetRequiredService<FiltroLlamaGuard>(),
                    sp.GetRequiredService<IOptions<FiltroSalidaOptions>>(), sp.GetRequiredService<ILogger<FiltroCompuesto>>()));
                break;
            case "modelo":
                services.AddScoped<IFiltroSalida>(sp => new FiltroCompuesto(sp.GetRequiredService<FiltroReglas>(),
                    new FiltroModelo(sp.GetRequiredService<IChatModel>()),
                    sp.GetRequiredService<IOptions<FiltroSalidaOptions>>(), sp.GetRequiredService<ILogger<FiltroCompuesto>>()));
                break;
            default:
                throw new InvalidOperationException($"FiltroSalida:Proveedor desconocido: {proveedor}. Use Reglas, LlamaGuard o Modelo.");
        }

        return services;
    }
}
