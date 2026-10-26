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
    ];
}
