using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Dpm;

namespace DpmAgent.Api.Agent;

/// <summary>
/// Herramientas que el agente puede usar. Todas son administrativas:
/// ninguna permite dar orientación clínica.
/// El paciente sobre el que se actúa sale siempre de la sesión (identificado por el teléfono
/// del canal), nunca de los argumentos del modelo.
/// </summary>
public sealed class AgentTools
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-DO");

    private static readonly JsonSerializerOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonArray Definiciones = BuildDefinitions();

    private readonly IDpmGateway _dpm;
    private readonly ServicioCarnet _carnets;

    public AgentTools(IDpmGateway dpm)
    {
        _dpm = dpm;
        _carnets = new ServicioCarnet(dpm);
    }

    public JsonArray Definitions => Definiciones;

    public async Task<string> ExecuteAsync(Sesion sesion, string name, string? argumentsJson, CancellationToken ct)
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
                "buscar_paciente" => await BuscarPaciente(sesion, ct),
                "registrar_paciente" => await RegistrarPaciente(sesion, args, ct),
                "consultar_disponibilidad" => await ConsultarDisponibilidad(args, ct),
                "crear_cita" => await CrearCita(sesion, args, ct),
                "citas_del_paciente" => await CitasDelPaciente(sesion, ct),
                "cancelar_cita" => await CancelarCita(sesion, Req(args, "cita_id"), ct),
                "confirmar_datos_carnet" => await ConfirmarCarnet(sesion, ct),
                "escalar_a_personal" => await Escalar(sesion, args, ct),
                _ => Error($"Herramienta desconocida: {name}"),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Error(ex.Message);
        }
    }

    private const string SinTelefono =
        "Esta conversación no tiene un teléfono verificado, así que no se puede identificar al paciente. Usa escalar_a_personal.";

    private const string SinPaciente =
        "Primero hay que identificar al paciente con buscar_paciente (o registrar_paciente si no aparece).";

    private static string PacienteDeSesion(Sesion sesion) =>
        sesion.PacienteId ?? throw new InvalidOperationException(SinPaciente);

    private async Task<string> ListarMedicos(CancellationToken ct)
    {
        var medicos = await _dpm.ListarMedicosAsync(ct);
        return Ok(new { medicos = medicos.Select(m => new { id = m.Id, nombre = m.Nombre, especialidad = m.Especialidad }) });
    }

    private async Task<string> BuscarPaciente(Sesion sesion, CancellationToken ct)
    {
        if (sesion.TelefonoCanal is null)
            return Error(SinTelefono);

        var p = await _dpm.BuscarPacienteAsync(sesion.TelefonoCanal, ct);
        if (p is null || !Telefono.Iguales(p.Telefono, sesion.TelefonoCanal))
            return Ok(new { encontrado = false });

        sesion.PacienteId = p.Id;
        return Ok(new { encontrado = true, paciente = new { id = p.Id, nombre = p.Nombre, ars = p.Ars } });
    }

    private async Task<string> RegistrarPaciente(Sesion sesion, JsonObject args, CancellationToken ct)
    {
        if (sesion.TelefonoCanal is null)
            return Error(SinTelefono);

        if (sesion.PacienteId is not null)
            return Error($"Esta conversación ya está vinculada al paciente {sesion.PacienteId}.");

        var existente = await _dpm.BuscarPacienteAsync(sesion.TelefonoCanal, ct);
        if (existente is not null && Telefono.Iguales(existente.Telefono, sesion.TelefonoCanal))
            return Error("Ya hay un paciente registrado con este teléfono. Usa buscar_paciente.");

        var p = await _dpm.RegistrarPacienteAsync(Req(args, "nombre"), sesion.TelefonoCanal, Opt(args, "ars"), ct);
        sesion.PacienteId = p.Id;
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

    private async Task<string> CrearCita(Sesion sesion, JsonObject args, CancellationToken ct)
    {
        var pacienteId = PacienteDeSesion(sesion);
        var fechaHora = DateTimeOffset.Parse(Req(args, "fecha_hora"), CultureInfo.InvariantCulture);
        var cita = await _dpm.CrearCitaAsync(pacienteId, Req(args, "medico_id"), fechaHora, Req(args, "motivo"), ct);

        return Ok(new { creada = true, cita = Resumen(cita) });
    }

    private async Task<string> CitasDelPaciente(Sesion sesion, CancellationToken ct)
    {
        var citas = await _dpm.CitasDePacienteAsync(PacienteDeSesion(sesion), ct);
        return Ok(new { citas = citas.Select(Resumen) });
    }

    private async Task<string> CancelarCita(Sesion sesion, string citaId, CancellationToken ct)
    {
        var cita = await _dpm.CancelarCitaAsync(citaId, PacienteDeSesion(sesion), ct);
        return cita is null
            ? Error($"No encontré la cita {citaId} entre las citas de este paciente.")
            : Ok(new { cancelada = true, cita = Resumen(cita) });
    }

    private async Task<string> ConfirmarCarnet(Sesion sesion, CancellationToken ct)
    {
        var carnet = await _dpm.UltimoCarnetPendienteAsync(sesion.Id, ct);
        if (carnet is null || !Telefono.Iguales(carnet.TelefonoCanal, sesion.TelefonoCanal))
            return Error("No hay datos de carnet pendientes de confirmar en esta conversación.");

        var r = await _carnets.ConfirmarAsync(
            carnet, null, ServicioCarnet.PorPaciente, sesion.PacienteId, exigirMismoNombre: true, ct);
        sesion.PacienteId = r.Paciente.Id;

        return Ok(new
        {
            confirmado = true,
            paciente_nuevo = r.PacienteNuevo,
            paciente = new { id = r.Paciente.Id, nombre = r.Paciente.Nombre, ars = r.Paciente.Ars, numero_afiliado = r.Paciente.NumeroAfiliado },
        });
    }

    private async Task<string> Escalar(Sesion sesion, JsonObject args, CancellationToken ct)
    {
        var urgente = args["urgente"]?.GetValueKind() == JsonValueKind.True;
        var e = await _dpm.RegistrarEscalamientoAsync(sesion.Id, sesion.PacienteId, Req(args, "motivo"), urgente, ct);
        return Ok(new { escalado = true, id = e.Id, urgente });
    }

    private static object Resumen(Cita c) => new
    {
        id = c.Id,
        paciente_id = c.PacienteId,
        medico_id = c.MedicoId,
        fecha_hora = Iso(c.FechaHora),
        texto = $"{TimeZoneInfo.ConvertTime(c.FechaHora, HorarioConsultorio.ZonaRd).ToString("dddd d 'de' MMMM", Es)}, {Hora(c.FechaHora)}",
        motivo = c.Motivo,
        estado = c.Estado,
    };

    private static string Iso(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, HorarioConsultorio.ZonaRd).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Hora(DateTimeOffset d) =>
        TimeZoneInfo.ConvertTime(d, HorarioConsultorio.ZonaRd).ToString("h:mm tt", Es);

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
            Tool("buscar_paciente",
                "Identifica al paciente por el número de teléfono desde el que escribe (lo pone el sistema, no hay que pedirlo). Usarla al inicio de la conversación.",
                new JsonObject()),
            Tool("registrar_paciente",
                "Registra como paciente nuevo a quien escribe desde este número. Usarla solo si buscar_paciente no lo encontró y después de pedir el nombre completo.",
                new JsonObject
                {
                    ["nombre"] = Str("Nombre completo."),
                    ["ars"] = Str("Nombre de la ARS (seguro), si tiene."),
                },
                "nombre"),
            Tool("consultar_disponibilidad", "Devuelve los horarios libres de un médico para una fecha.",
                new JsonObject
                {
                    ["medico_id"] = Str("Id del médico, por ejemplo MED-01."),
                    ["fecha"] = Str("Fecha en formato YYYY-MM-DD."),
                },
                "medico_id", "fecha"),
            Tool("crear_cita", "Crea una cita para el paciente identificado. Solo usarla después de que confirme el horario.",
                new JsonObject
                {
                    ["medico_id"] = Str("Id del médico."),
                    ["fecha_hora"] = Str("Valor fecha_hora exacto devuelto por consultar_disponibilidad."),
                    ["motivo"] = Str("Motivo administrativo breve, por ejemplo 'consulta de control' o 'primera consulta'. Sin detalles clínicos."),
                },
                "medico_id", "fecha_hora", "motivo"),
            Tool("citas_del_paciente", "Lista las citas confirmadas del paciente identificado.", new JsonObject()),
            Tool("cancelar_cita", "Cancela una cita del paciente identificado. Confirmar con el paciente antes de usarla.",
                new JsonObject { ["cita_id"] = Str("Id de la cita, por ejemplo CITA-1001.") },
                "cita_id"),
            Tool("confirmar_datos_carnet",
                "Guarda los datos del carnet de ARS que el paciente envió en foto (ARS y número de afiliado; si es nuevo, lo registra). Usarla solo después de mostrarle los datos y que diga que están correctos.",
                new JsonObject()),
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
