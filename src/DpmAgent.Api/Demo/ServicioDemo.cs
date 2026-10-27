using DpmAgent.Api.Agenda;
using DpmAgent.Api.Data;
using DpmAgent.Api.Data.Entidades;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Panel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DpmAgent.Api.Demo;

/// <summary>Un botón de ejemplo de la página de demo.</summary>
/// <param name="Etiqueta">Texto del botón por idioma de la interfaz (es, en).</param>
/// <param name="Mensaje">Mensaje que se envía por idioma de la interfaz.</param>
/// <param name="Nota">Indicación que se muestra al usarlo, por idioma de la interfaz.</param>
public sealed record EjemploDemo(
    string Id, string Telefono, Dictionary<string, string> Etiqueta, Dictionary<string, string> Mensaje, Dictionary<string, string>? Nota = null);

public sealed record InfoDemo(bool Habilitado, IReadOnlyList<EjemploDemo> Ejemplos, string? UsuarioPanel, string? ClavePanel);

/// <summary>
/// Escenario y reinicio de la demo. El reinicio solo borra datos del consultorio de demo
/// y se niega si ese consultorio no está marcado como de demostración.
/// </summary>
public sealed class ServicioDemo
{
    public const string UsuarioPanel = "demo";

    private readonly DpmDbContext _db;
    private readonly DemoOptions _opciones;
    private readonly TimeProvider _clock;
    private readonly ServicioUsuarios _usuarios;
    private readonly ServicioListaEspera _lista;
    private readonly IDpmGateway _dpm;

    public ServicioDemo(
        DpmDbContext db, IOptions<DemoOptions> opciones, TimeProvider clock, ServicioUsuarios usuarios, ServicioListaEspera lista, IDpmGateway dpm)
    {
        _db = db;
        _opciones = opciones.Value;
        _clock = clock;
        _usuarios = usuarios;
        _lista = lista;
        _dpm = dpm;
    }

    public InfoDemo Info() => _opciones.Habilitado
        ? new InfoDemo(true, Ejemplos, string.IsNullOrWhiteSpace(_opciones.ClavePanel) ? null : UsuarioPanel, _opciones.ClavePanel)
        : new InfoDemo(false, [], null, null);

    /// <summary>
    /// Deja la demo lista para probar: María con una cita el próximo día hábil a las 9:00 y José en la lista
    /// de espera de ese turno, con conversaciones web fijas ("demo-" + teléfono) para que la oferta se vea en el chat.
    /// </summary>
    public async Task PrepararEscenarioAsync(CancellationToken ct)
    {
        var consultorio = await ConsultorioDemoAsync(ct);
        if (await _db.Citas.AnyAsync(ct))
            return;

        var dia = ProximoDiaHabil();
        var maria = (await _dpm.BuscarPacienteAsync(DatosDemo.TelefonoMaria, ct))!;
        var jose = (await _dpm.BuscarPacienteAsync(DatosDemo.TelefonoJose, ct))!;
        var medico = (await _dpm.ListarMedicosAsync(ct))[0];

        await _dpm.CrearCitaAsync(maria.Id, medico.Id, HorarioConsultorio.EnHoraRd(dia, new TimeOnly(9, 0)), "control", ct);
        await _lista.AgregarAsync(jose.Id, medico.Id, dia, dia.AddDays(3), new TimeOnly(8, 0), new TimeOnly(12, 0), ct);

        var ahora = _clock.GetUtcNow().UtcDateTime;
        foreach (var p in new[] { maria, jose })
        {
            _db.Conversaciones.Add(new Conversacion
            {
                ConsultorioId = consultorio.Id,
                SesionId = SesionDemo(p.Telefono),
                TelefonoCanal = p.Telefono,
                PacienteId = IdsDpm.Leer(p.Id, "PAC"),
                CreadaUtc = ahora,
                ActualizadaUtc = ahora,
            });
        }

        // Seguimientos indicados por la doctora: el de María es en 6 meses; el de José entra hoy en la ventana de
        // contacto, así que el motor le escribe para escoger horario.
        var hoy = HorarioConsultorio.FechaRd(_clock.GetUtcNow());
        foreach (var (paciente, fecha) in new[] { (maria, hoy.AddMonths(6)), (jose, hoy.AddDays(consultorio.DiasContactoSeguimiento)) })
        {
            _db.Seguimientos.Add(new Seguimiento
            {
                ConsultorioId = consultorio.Id, PacienteId = IdsDpm.Leer(paciente.Id, "PAC")!.Value, MedicoId = IdsDpm.Leer(medico.Id, "MED")!.Value,
                FechaObjetivo = fecha, ToleranciaDias = 7, Motivo = "control", Estado = ServicioSeguimientos.Pendiente,
                CreadoPor = "medico:" + medico.Id, CreadoUtc = ahora,
            });
        }

        // El Dr. Luis Herrera (ficticio) ya verificó su número: en la demo se le puede escribir como médico desde el chat web.
        var herrera = await _db.Medicos.OrderBy(m => m.Id).Skip(1).FirstAsync(ct);
        herrera.Telefono = DatosDemo.TelefonoMedico;
        herrera.TelefonoVerificadoUtc = ahora;

        // Urgencias: un cupo reservado a las 16:30 por médico y día hábil (no se libera en la demo) y centros de emergencia ficticios.
        consultorio.HoraLiberarCuposUrgencia = 17;
        consultorio.CentrosEmergencia = "Hospital Ejemplo Norte (ficticio)\nClínica Ejemplo Sur (ficticia)";
        foreach (var m in await _db.Medicos.Select(m => m.Id).ToListAsync(ct))
        {
            foreach (var diaSemana in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
                _db.CuposUrgencia.Add(new CupoUrgencia { ConsultorioId = consultorio.Id, MedicoId = m, DiaSemana = diaSemana, Hora = new TimeOnly(16, 30) });
        }

        await _db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(_opciones.ClavePanel) && !await _db.UsuariosPanel.AnyAsync(u => u.Usuario == UsuarioPanel, ct))
            await _usuarios.CrearAsync(consultorio.Id, UsuarioPanel, "Demo (ficticia)", _opciones.ClavePanel, "secretaria", ct);
    }

    /// <summary>Borra todo lo del consultorio de demo (menos los usuarios del panel) y vuelve a sembrarlo.</summary>
    public async Task ReiniciarAsync(CancellationToken ct)
    {
        var consultorio = await ConsultorioDemoAsync(ct);
        var id = consultorio.Id;

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var conversaciones = _db.Conversaciones.IgnoreQueryFilters().Where(c => c.ConsultorioId == id).Select(c => c.Id);
            await _db.Mensajes.Where(m => conversaciones.Contains(m.ConversacionId)).ExecuteDeleteAsync(ct);
            await _db.Conversaciones.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.MensajesSalientes.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.MensajesEntrantes.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.OfertasHueco.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.ListaEspera.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.AutorizacionesArs.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.CarnetsPendientes.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Consentimientos.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Seguimientos.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.SolicitudesSobrecupo.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.CuposUrgencia.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.BloqueosAgenda.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Feriados.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Escalamientos.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Bitacora.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Citas.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Pacientes.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);
            await _db.Medicos.IgnoreQueryFilters().Where(x => x.ConsultorioId == id).ExecuteDeleteAsync(ct);

            await DatosDemo.SembrarPersonasAsync(_db, id, _clock, ct);
            await tx.CommitAsync(ct);
        }

        _db.ChangeTracker.Clear();
        await PrepararEscenarioAsync(ct);
    }

    /// <summary>Sesión web fija por teléfono en la demo: así la oferta automática aparece en el chat de José.</summary>
    public static string SesionDemo(string telefono) => "demo-" + Telefono.Normalizar(telefono);

    private async Task<Consultorio> ConsultorioDemoAsync(CancellationToken ct)
    {
        if (!_opciones.Habilitado)
            throw new InvalidOperationException("La demo no está habilitada.");

        // El consultorio es el del ámbito (el del chat público); solo se toca si está marcado como demo.
        var consultorio = await _db.Consultorios.FirstOrDefaultAsync(c => c.Id == _db.ConsultorioId, ct);
        if (consultorio is not { EsDemo: true })
            throw new InvalidOperationException($"El consultorio {_db.ConsultorioId} no está marcado como de demostración: no se reinicia.");
        return consultorio;
    }

    private DateOnly ProximoDiaHabil()
    {
        var dia = HorarioConsultorio.FechaRd(_clock.GetUtcNow()).AddDays(1);
        while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            dia = dia.AddDays(1);
        return dia;
    }

    private static readonly IReadOnlyList<EjemploDemo> Ejemplos =
    [
        new("ingles", DatosDemo.TelefonoMaria,
            new() { ["es"] = "🇺🇸 Pedir cita en inglés", ["en"] = "🇺🇸 Book in English" },
            new() { ["es"] = "Hi! I'd like an appointment with the doctor tomorrow morning.", ["en"] = "Hi! I'd like an appointment with the doctor tomorrow morning." }),
        new("espanol", DatosDemo.TelefonoMaria,
            new() { ["es"] = "🇩🇴 Pedir cita en español", ["en"] = "🇩🇴 Book in Spanish" },
            new() { ["es"] = "Hola, quiero una cita con la doctora para mañana en la tarde.", ["en"] = "Hola, quiero una cita con la doctora para mañana en la tarde." }),
        new("kreyol", DatosDemo.TelefonoJose,
            new() { ["es"] = "🇭🇹 Pedir cita en kreyòl", ["en"] = "🇭🇹 Book in Haitian Creole" },
            new() { ["es"] = "Bonjou, mwen vle pran yon randevou ak doktè a demen maten.", ["en"] = "Bonjou, mwen vle pran yon randevou ak doktè a demen maten." }),
        new("clinica", DatosDemo.TelefonoMaria,
            new() { ["es"] = "💊 Pregunta clínica", ["en"] = "💊 Clinical question" },
            new() { ["es"] = "¿Cuánto ibuprofeno le puedo dar a mi hijo de 5 años para la fiebre?", ["en"] = "How much ibuprofen can I give my 5-year-old son for his fever?" },
            new() { ["es"] = "Debe escalarlo al personal sin dar dosis.", ["en"] = "It must escalate to the staff without giving a dose." }),
        new("emergencia", DatosDemo.TelefonoMaria,
            new() { ["es"] = "🚑 Emergencia", ["en"] = "🚑 Emergency" },
            new() { ["es"] = "Mi papá tiene un dolor muy fuerte en el pecho y le falta el aire.", ["en"] = "My father has a severe chest pain and can't breathe." },
            new() { ["es"] = "Debe indicar el 911 y escalar como urgente.", ["en"] = "It must point to 911 and escalate as urgent." }),
        new("cancelar", DatosDemo.TelefonoMaria,
            new() { ["es"] = "↩️ Cancelar y ver la lista de espera", ["en"] = "↩️ Cancel and watch the waiting list" },
            new() { ["es"] = "Necesito cancelar mi cita de mañana.", ["en"] = "I need to cancel my appointment tomorrow." },
            new()
            {
                ["es"] = "Confirme con \"sí\". José (829-555-0102) está en la lista de espera: luego use \"Pedir cita en kreyòl\" o escriba con su número para ver la oferta del turno liberado.",
                ["en"] = "Confirm with \"yes\". José (829-555-0102) is on the waiting list: then use \"Book in Haitian Creole\" or write from his number to see the offer for the freed slot.",
            }),
        new("urgente", DatosDemo.TelefonoMaria,
            new() { ["es"] = "⏱️ Que me vean hoy", ["en"] = "⏱️ See me today" },
            new() { ["es"] = "Necesito que me vean hoy, por favor.", ["en"] = "I need to be seen today, please." },
            new()
            {
                ["es"] = "No pregunta síntomas ni juzga la gravedad: ofrece el primer horario o el cupo de urgencia de las 4:30 p. m.; si no hay, crea una solicitud de sobrecupo que solo la secretaria (panel → Urgencias) o el médico aprueban. Siempre recuerda el 911; de noche, también los centros de emergencia.",
                ["en"] = "It never asks about symptoms or judges severity: it offers the first slot or the 4:30 PM urgent slot; if there is none, it files an extra-slot request that only the receptionist (dashboard → Urgent requests) or the doctor can approve. It always reminds of 911; at night, also the emergency centers.",
            }),
        new("medico", DatosDemo.TelefonoMedico,
            new() { ["es"] = "🩺 El médico pide un control", ["en"] = "🩺 The doctor orders a follow-up" },
            new() { ["es"] = "Control para María en 3 meses", ["en"] = "Follow-up for María in 3 months" },
            new()
            {
                ["es"] = "Escribe como el Dr. Luis Herrera (ficticio), con su número verificado 849-555-0111. Confirme con \"sí\": el seguimiento aparece en el panel (Seguimientos) y se le avisa a María. Solo se guardan la fecha y el motivo administrativo.",
                ["en"] = "Writes as Dr. Luis Herrera (ficticio), from his verified number 849-555-0111. Confirm with \"yes\": the follow-up shows in the dashboard (Follow-ups) and María is notified. Only the date and the administrative reason are stored.",
            }),
    ];
}
