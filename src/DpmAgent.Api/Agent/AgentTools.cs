using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Agent;

public sealed record Escalamiento(string Id, string SesionId, string Motivo, bool Urgente, DateTimeOffset Creado);

/// <summary>
/// Herramientas que el agente puede usar. Todas son administrativas:
/// ninguna permite dar orientación clínica.
/// </summary>
public sealed class AgentTools
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-DO");

    private static readonly JsonSerializerOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IDpmGateway _dpm;
    private readonly ConcurrentQueue<Escalamiento> _escalamientos = new();
    private int _secEscalamiento;

    public AgentTools(IDpmGateway dpm) => _dpm = dpm;

    public IReadOnlyCollection<Escalamiento> Escalamientos => _escalamientos.ToArray();

    public JsonArray Definitions { get; } = BuildDefinitions();

    public async Task<string> ExecuteAsync(string sesionId, string name, string? argumentsJson, CancellationToken ct)
    {
        JsonObject args;
        try
        {
            args = string.IsNullOrWhiteSpace(argumentsJson)
                ? new JsonObject()
                : JsonNode.Parse(argumentsJson)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return Error("Los argumentos no son JSON válido.");
        }

        try
        {
            return name switch
            {
                "listar_medicos" => await ListarMedicos(ct),
                "buscar_paciente" => await BuscarPaciente(Req(args, "telefono_o_id"), ct),
                "registrar_paciente" => await RegistrarPaciente(args, ct),
                "consultar_disponibilidad" => await ConsultarDisponibilidad(args, ct),
                "crear_cita" => await CrearCita(args, ct),
                "citas_del_paciente" => await CitasDelPaciente(Req(args, "paciente_id"), ct),
                "cancelar_cita" => await CancelarCita(Req(args, "cita_id"), ct),
                "escalar_a_personal" => Escalar(sesionId, args),
                _ => Error($"Herramienta desconocida: {name}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Error(ex.Message);
        }
    }

    private async Task<string> ListarMedicos(CancellationToken ct)
    {
        var medicos = await _dpm.ListarMedicosAsync(ct);
        return Ok(new { medicos = medicos.Select(m => new { id = m.Id, nombre = m.Nombre, especialidad = m.Especialidad }) });
    }

    private async Task<string> BuscarPaciente(string clave, CancellationToken ct)
    {
        var p = await _dpm.BuscarPacienteAsync(clave, ct);
        return p is null
            ? Ok(new { encontrado = false })
            : Ok(new { encontrado = true, paciente = new { id = p.Id, nombre = p.Nombre, ars = p.Ars } });
    }

    private async Task<string> RegistrarPaciente(JsonObject args, CancellationToken ct)
    {
        var p = await _dpm.RegistrarPacienteAsync(Req(args, "nombre"), Req(args, "telefono"), Opt(args, "ars"), ct);
        return Ok(new { registrado = true, paciente = new { id = p.Id, nombre = p.Nombre, ars = p.Ars } });
    }

    private async Task<string> ConsultarDisponibilidad(JsonObject args, CancellationToken ct)
    {
        var medicoId = Req(args, "medico_id");
        var fecha = DateOnly.ParseExact(Req(args, "fecha"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var slots = await _dpm.ConsultarDisponibilidadAsync(medicoId, fecha, ct);

        return Ok(new
        {
            medico_id = medicoId,
            fecha = fecha.ToString("yyyy-MM-dd"),
            dia = fecha.ToString("dddd d 'de' MMMM", Es),
            horarios = slots.Select(s => new { fecha_hora = Iso(s), texto = Hora(s) }),
        });
    }

    private async Task<string> CrearCita(JsonObject args, CancellationToken ct)
    {
        var fechaHora = DateTimeOffset.Parse(Req(args, "fecha_hora"), CultureInfo.InvariantCulture);
        var cita = await _dpm.CrearCitaAsync(
            Req(args, "paciente_id"), Req(args, "medico_id"), fechaHora, Req(args, "motivo"), ct);

        return Ok(new { creada = true, cita = Resumen(cita) });
    }

    private async Task<string> CitasDelPaciente(string pacienteId, CancellationToken ct)
    {
        var citas = await _dpm.CitasDePacienteAsync(pacienteId, ct);
        return Ok(new { citas = citas.Select(Resumen) });
    }

    private async Task<string> CancelarCita(string citaId, CancellationToken ct)
    {
        var cita = await _dpm.CancelarCitaAsync(citaId, ct);
        return cita is null ? Error($"No existe la cita {citaId}.") : Ok(new { cancelada = true, cita = Resumen(cita) });
    }

    private string Escalar(string sesionId, JsonObject args)
    {
        var urgente = args["urgente"]?.GetValueKind() == JsonValueKind.True;
        var e = new Escalamiento(
            $"ESC-{Interlocked.Increment(ref _secEscalamiento)}",
            sesionId,
            Req(args, "motivo"),
            urgente,
            DateTimeOffset.UtcNow);

        _escalamientos.Enqueue(e);
        return Ok(new { escalado = true, id = e.Id, urgente });
    }

    private static object Resumen(Cita c) => new
    {
        id = c.Id,
        paciente_id = c.PacienteId,
        medico_id = c.MedicoId,
        fecha_hora = Iso(c.FechaHora),
        texto = $"{TimeZoneInfo.ConvertTime(c.FechaHora, MockDpmGateway.ZonaRd).ToString("dddd d 'de' MMMM", Es)}, {Hora(c.FechaHora)}",
        motivo = c.Motivo,
        estado = c.Estado,
    };

    private static string Iso(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, MockDpmGateway.ZonaRd).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Hora(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, MockDpmGateway.ZonaRd).ToString("h:mm tt", Es);

    private static string Req(JsonObject args, string key)
    {
        var value = args[key]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Falta el parámetro '{key}'.");
        return value.Trim();
    }

    private static string? Opt(JsonObject args, string key) => args[key]?.ToString();

    private static string Ok(object value) => JsonSerializer.Serialize(value, JsonOpts);

    private static string Error(string message) => JsonSerializer.Serialize(new { error = message }, JsonOpts);

    private static JsonArray BuildDefinitions()
    {
        static JsonObject Tool(string name, string description, JsonObject properties, params string[] required) => new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = properties,
                    ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
                    ["additionalProperties"] = false,
                },
            },
        };

        static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

        return
        [
            Tool("listar_medicos", "Lista los médicos del consultorio con su id y especialidad.", new JsonObject()),
            Tool("buscar_paciente", "Busca un paciente registrado por número de teléfono o id de paciente.",
                new JsonObject { ["telefono_o_id"] = Str("Teléfono (solo dígitos) o id tipo PAC-001.") },
                "telefono_o_id"),
            Tool("registrar_paciente", "Registra un paciente nuevo. Pedir nombre completo y teléfono antes de usarla.",
                new JsonObject
                {
                    ["nombre"] = Str("Nombre completo."),
                    ["telefono"] = Str("Teléfono de contacto."),
                    ["ars"] = Str("Nombre de la ARS (seguro), si tiene."),
                },
                "nombre", "telefono"),
            Tool("consultar_disponibilidad", "Devuelve los horarios libres de un médico para una fecha.",
                new JsonObject
                {
                    ["medico_id"] = Str("Id del médico, por ejemplo MED-01."),
                    ["fecha"] = Str("Fecha en formato YYYY-MM-DD."),
                },
                "medico_id", "fecha"),
            Tool("crear_cita", "Crea una cita. Solo usarla después de que el paciente confirme el horario.",
                new JsonObject
                {
                    ["paciente_id"] = Str("Id del paciente."),
                    ["medico_id"] = Str("Id del médico."),
                    ["fecha_hora"] = Str("Valor fecha_hora exacto devuelto por consultar_disponibilidad."),
                    ["motivo"] = Str("Motivo administrativo breve, por ejemplo 'consulta de control' o 'primera consulta'. Sin detalles clínicos."),
                },
                "paciente_id", "medico_id", "fecha_hora", "motivo"),
            Tool("citas_del_paciente", "Lista las citas confirmadas de un paciente.",
                new JsonObject { ["paciente_id"] = Str("Id del paciente.") },
                "paciente_id"),
            Tool("cancelar_cita", "Cancela una cita existente. Confirmar con el paciente antes de usarla.",
                new JsonObject { ["cita_id"] = Str("Id de la cita, por ejemplo CITA-1001.") },
                "cita_id"),
            Tool("escalar_a_personal",
                "Pasa la conversación al personal humano del consultorio. Usarla para cualquier pregunta clínica, síntomas, medicamentos, resultados, quejas o lo que no se pueda resolver.",
                new JsonObject
                {
                    ["motivo"] = Str("Resumen breve de lo que necesita el paciente."),
                    ["urgente"] = new JsonObject { ["type"] = "boolean", ["description"] = "true si el paciente describe una posible emergencia." },
                },
                "motivo", "urgente"),
        ];
    }
}
