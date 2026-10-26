using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Canales;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

/// <summary>
/// Lista de espera, ofertas de huecos, recordatorios, bitácora y despacho, sobre SQLite real.
/// Cada llamada a <see cref="Peticion"/> usa un contexto nuevo, como una petición aparte.
/// </summary>
public sealed class AgendaTests : IDisposable
{
    // Lunes 26 de octubre de 2026, 12:00 p. m. en RD.
    private static readonly DateTimeOffset Inicio = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly DateTimeOffset Martes9 = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0));

    private readonly RelojAjustable _reloj = new(Inicio);
    private readonly SqliteDePrueba _db;
    private readonly DespertadorAgenda _despertador = new();
    private readonly CanalWhatsAppFalso _whatsApp = new();

    public AgendaTests() => _db = new SqliteDePrueba(_reloj);

    public void Dispose() => _db.Dispose();

    private sealed record Servicios(
        DpmDbContext Db, DbDpmGateway Dpm, AgentTools Tools, ServicioListaEspera Lista, MotorAgenda Motor, Bitacora Bitacora, DespachadorMensajes Despachador);

    private Servicios Peticion()
    {
        var db = _db.Contexto();
        var opciones = Options.Create(new AgendaOptions());
        var dpm = new DbDpmGateway(db, _reloj);
        var bitacora = new Bitacora(db, _reloj);
        var lista = new ServicioListaEspera(db, dpm, _despertador, _reloj);
        var notificador = new Notificador(db, new DbConversationStore(db, _reloj), _despertador, _reloj);
        return new Servicios(
            db,
            dpm,
            new AgentTools(dpm, lista, bitacora),
            lista,
            new MotorAgenda(db, notificador, bitacora, opciones, _reloj, NullLogger<MotorAgenda>.Instance),
            bitacora,
            new DespachadorMensajes(db, [new WebChatChannel(), _whatsApp], opciones, _reloj, NullLogger<DespachadorMensajes>.Instance));
    }

    private async Task<Sesion> Identificado(string telefono, string? sesionId = null)
    {
        var sesion = new Sesion(sesionId ?? CanalesConocidos.SesionWhatsApp(telefono));
        sesion.VincularTelefono(telefono);
        await Peticion().Tools.ExecuteAsync(sesion, "buscar_paciente", "{}", CancellationToken.None);
        Assert.NotNull(sesion.PacienteId);
        return sesion;
    }

    private Task<string> Herramienta(Sesion sesion, string nombre, string args = "{}") =>
        Peticion().Tools.ExecuteAsync(sesion, nombre, args, CancellationToken.None);

    private Task<string> AnotarEnLista(Sesion sesion, string medico = "MED-01") =>
        Herramienta(sesion, "agregar_lista_espera",
            $$"""{"medico_id":"{{medico}}","desde":"2026-10-27","hasta":"2026-10-30","hora_desde":"08:00","hora_hasta":"12:00"}""");

    private async Task<string> CitaDeMariaMartes9()
    {
        var cita = await Peticion().Dpm.CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);
        return cita.Id;
    }

    private async Task<string> RegistrarRosa()
    {
        var p = await Peticion().Dpm.RegistrarPacienteAsync("Rosa Peña (ficticia)", "8495550123", null, CancellationToken.None);
        return p.Id;
    }

    [Fact]
    public async Task Anota_al_paciente_en_la_lista_y_actualiza_si_ya_estaba()
    {
        var jose = await Identificado("8295550102");

        var r1 = await AnotarEnLista(jose);
        var r2 = await Herramienta(jose, "agregar_lista_espera",
            """{"medico_id":"MED-01","desde":"2026-10-28","hasta":"2026-11-05","hora_desde":"14:00","hora_hasta":"17:00"}""");

        Assert.Contains("\"en_lista\":true", r1);
        Assert.Contains("\"hora_desde\":\"14:00\"", r2);
        var activa = Assert.Single(await Peticion().Lista.ListarActivasAsync(CancellationToken.None));
        Assert.Equal("PAC-002", activa.PacienteId);
        Assert.Equal(new DateOnly(2026, 11, 5), activa.Hasta);
    }

    [Theory]
    [InlineData("""{"medico_id":"MED-01","desde":"2026-10-20","hasta":"2026-10-30","hora_desde":"08:00","hora_hasta":"12:00"}""", "anterior a hoy")]
    [InlineData("""{"medico_id":"MED-01","desde":"2026-10-28","hasta":"2026-10-27","hora_desde":"08:00","hora_hasta":"12:00"}""", "posterior")]
    [InlineData("""{"medico_id":"MED-01","desde":"2026-10-27","hasta":"2027-03-01","hora_desde":"08:00","hora_hasta":"12:00"}""", "60 días")]
    [InlineData("""{"medico_id":"MED-01","desde":"2026-10-27","hasta":"2026-10-30","hora_desde":"12:00","hora_hasta":"08:00"}""", "rango de horas")]
    [InlineData("""{"medico_id":"MED-99","desde":"2026-10-27","hasta":"2026-10-30","hora_desde":"08:00","hora_hasta":"12:00"}""", "No existe el médico")]
    [InlineData("""{"medico_id":"MED-01","desde":"27/10/2026","hasta":"2026-10-30","hora_desde":"08:00","hora_hasta":"12:00"}""", "error")]
    public async Task Valida_los_datos_de_la_lista_de_espera(string args, string error)
    {
        var jose = await Identificado("8295550102");

        var r = await Herramienta(jose, "agregar_lista_espera", args);

        Assert.Contains("\"error\"", r);
        Assert.Contains(error, r);
        Assert.Empty(await Peticion().Lista.ListarActivasAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Al_cancelar_se_ofrece_el_hueco_y_el_paciente_lo_acepta()
    {
        var citaMaria = await CitaDeMariaMartes9();
        var jose = await Identificado("8295550102");
        await AnotarEnLista(jose);
        var maria = await Identificado("8095550101", "web-maria");

        await Herramienta(maria, "cancelar_cita", $$"""{"cita_id":"{{citaMaria}}"}""");
        var r = await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        Assert.Equal(1, r.OfertasEnviadas);

        // José no tenía conversación: se le escribe por WhatsApp y la oferta queda en su historial.
        var historial = await new DbConversationStore(_db.Contexto(), _reloj).ObtenerAsync(jose.Id, CancellationToken.None);
        Assert.Contains("Se liberó un turno", historial.Mensajes[^1]["content"]!.ToString());
        await Peticion().Despachador.DespacharPendientesAsync(CancellationToken.None);
        var enviado = Assert.Single(_whatsApp.Enviados);
        Assert.Equal("8295550102", enviado.Destino);
        Assert.Equal("oferta", enviado.Tipo);

        var acepta = await Herramienta(jose, "responder_oferta", """{"acepta":true}""");

        Assert.Contains("\"aceptada\":true", acepta);
        var citasJose = await Peticion().Dpm.CitasDePacienteAsync("PAC-002", CancellationToken.None);
        Assert.Equal(Martes9, Assert.Single(citasJose).FechaHora);
        Assert.Empty(await Peticion().Lista.ListarActivasAsync(CancellationToken.None));

        // La siguiente vuelta no vuelve a ofrecer nada.
        var r2 = await Peticion().Motor.ProcesarAsync(CancellationToken.None);
        Assert.Equal(0, r2.OfertasEnviadas);

        var acciones = (await Peticion().Bitacora.ListarAsync(50, CancellationToken.None)).Select(b => b.Accion).ToList();
        Assert.Contains("cita_cancelada", acciones);
        Assert.Contains("oferta_enviada", acciones);
        Assert.Contains("oferta_aceptada", acciones);
    }

    [Fact]
    public async Task Si_rechaza_se_ofrece_al_siguiente_en_orden_de_llegada()
    {
        var citaMaria = await CitaDeMariaMartes9();
        var jose = await Identificado("8295550102");
        await AnotarEnLista(jose);
        await RegistrarRosa();
        _reloj.Avanzar(TimeSpan.FromMinutes(1));
        var rosa = await Identificado("8495550123");
        await AnotarEnLista(rosa);

        await Peticion().Dpm.CancelarCitaAsync(citaMaria, "PAC-001", CancellationToken.None);
        await Peticion().Motor.ProcesarAsync(CancellationToken.None);
        await Herramienta(jose, "responder_oferta", """{"acepta":false}""");
        await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        var ofertas = await _db.Contexto().OfertasHueco.OrderBy(o => o.Id).ToListAsync();
        Assert.Equal(["rechazada", "pendiente"], ofertas.Select(o => o.Estado));
        Assert.Equal(IdsDpm.Leer(rosa.PacienteId, "PAC"), ofertas[1].PacienteId);
        // José rechazó este hueco, pero sigue en la lista para otros.
        Assert.Equal(2, (await Peticion().Lista.ListarActivasAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Una_oferta_vencida_avisa_al_paciente_y_pasa_al_siguiente()
    {
        var citaMaria = await CitaDeMariaMartes9();
        var jose = await Identificado("8295550102");
        await AnotarEnLista(jose);
        await RegistrarRosa();
        var rosa = await Identificado("8495550123");
        await AnotarEnLista(rosa);

        await Peticion().Dpm.CancelarCitaAsync(citaMaria, "PAC-001", CancellationToken.None);
        await Peticion().Motor.ProcesarAsync(CancellationToken.None);
        _reloj.Avanzar(TimeSpan.FromMinutes(31));

        var r = await Peticion().Motor.ProcesarAsync(CancellationToken.None);
        var tarde = await Herramienta(jose, "responder_oferta", """{"acepta":true}""");

        Assert.Equal(1, r.OfertasVencidas);
        Assert.Equal(1, r.OfertasEnviadas);
        Assert.Contains("No hay ninguna oferta", tarde);
        var historialJose = await new DbConversationStore(_db.Contexto(), _reloj).ObtenerAsync(jose.Id, CancellationToken.None);
        Assert.Contains("venció el tiempo", historialJose.Mensajes[^1]["content"]!.ToString());
        Assert.Empty(await Peticion().Dpm.CitasDePacienteAsync("PAC-002", CancellationToken.None));
    }

    [Fact]
    public async Task Aceptar_despues_del_vencimiento_falla_aunque_el_motor_no_haya_pasado()
    {
        var citaMaria = await CitaDeMariaMartes9();
        var jose = await Identificado("8295550102");
        await AnotarEnLista(jose);
        await Peticion().Dpm.CancelarCitaAsync(citaMaria, "PAC-001", CancellationToken.None);
        await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        _reloj.Avanzar(TimeSpan.FromMinutes(45));
        var r = await Herramienta(jose, "responder_oferta", """{"acepta":true}""");

        Assert.Contains("venció", r);
        Assert.Empty(await Peticion().Dpm.CitasDePacienteAsync("PAC-002", CancellationToken.None));
    }

    [Fact]
    public async Task Si_el_hueco_se_ocupo_mientras_tanto_la_oferta_queda_invalida()
    {
        var citaMaria = await CitaDeMariaMartes9();
        var jose = await Identificado("8295550102");
        await AnotarEnLista(jose);
        await Peticion().Dpm.CancelarCitaAsync(citaMaria, "PAC-001", CancellationToken.None);
        await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        // María se arrepiente y vuelve a reservar el mismo horario por otro lado.
        await Peticion().Dpm.CrearCitaAsync("PAC-001", "MED-01", Martes9, "control", CancellationToken.None);
        var r = await Herramienta(jose, "responder_oferta", """{"acepta":true}""");

        Assert.Contains("ya no está disponible", r);
        Assert.Equal("invalida", (await _db.Contexto().OfertasHueco.SingleAsync()).Estado);
    }

    [Fact]
    public async Task No_se_ofrecen_huecos_demasiado_cercanos_ni_al_que_cancelo()
    {
        // Hoy a las 12:30: faltan 30 minutos, menos que la anticipación mínima.
        var pronto = HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 26), new TimeOnly(14, 0));
        _reloj.Ahora = pronto.AddMinutes(-30);
        var citaHoy = await Peticion().Dpm.CrearCitaAsync("PAC-002", "MED-01", pronto, "control", CancellationToken.None);
        var jose = await Identificado("8295550102");
        await Herramienta(jose, "agregar_lista_espera",
            """{"medico_id":"MED-01","desde":"2026-10-26","hasta":"2026-10-30","hora_desde":"08:00","hora_hasta":"17:00"}""");
        await Peticion().Dpm.CancelarCitaAsync(citaHoy.Id, "PAC-002", CancellationToken.None);

        var cercano = await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        // El martes José cancela su propia cita: no se le ofrece a él mismo.
        var citaMartes = await Peticion().Dpm.CrearCitaAsync("PAC-002", "MED-01", Martes9, "control", CancellationToken.None);
        await Peticion().Dpm.CancelarCitaAsync(citaMartes.Id, "PAC-002", CancellationToken.None);
        var propio = await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        Assert.Equal(0, cercano.OfertasEnviadas);
        Assert.Equal(1, cercano.HuecosCerrados);
        Assert.Equal(0, propio.OfertasEnviadas);
        Assert.Empty(await _db.Contexto().OfertasHueco.ToListAsync());
    }

    [Fact]
    public async Task Un_paciente_no_recibe_dos_ofertas_a_la_vez()
    {
        var c1 = await CitaDeMariaMartes9();
        var c2 = (await Peticion().Dpm.CrearCitaAsync("PAC-001", "MED-01", Martes9.AddHours(1), "control", CancellationToken.None)).Id;
        var jose = await Identificado("8295550102");
        await AnotarEnLista(jose);

        await Peticion().Dpm.CancelarCitaAsync(c1, "PAC-001", CancellationToken.None);
        await Peticion().Dpm.CancelarCitaAsync(c2, "PAC-001", CancellationToken.None);
        var r = await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        Assert.Equal(1, r.OfertasEnviadas);
        Assert.Single(await _db.Contexto().OfertasHueco.ToListAsync());
    }

    [Fact]
    public async Task Recordatorio_24_horas_antes_una_sola_vez_y_el_paciente_confirma()
    {
        // Cita del martes 9:00 reservada el lunes a mediodía.
        var cita = await CitaDeMariaMartes9();
        var antes = await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        _reloj.Avanzar(TimeSpan.FromHours(7));
        var primero = await Peticion().Motor.ProcesarAsync(CancellationToken.None);
        var segundo = await Peticion().Motor.ProcesarAsync(CancellationToken.None);

        Assert.Equal(0, antes.Recordatorios); // recién reservada
        Assert.Equal(1, primero.Recordatorios);
        Assert.Equal(0, segundo.Recordatorios);

        var mensaje = await _db.Contexto().MensajesSalientes.SingleAsync();
        Assert.Equal("recordatorio", mensaje.Tipo);
        Assert.Contains(cita, mensaje.Texto);
        Assert.Contains("CONFIRMO", mensaje.Texto);

        var maria = await Identificado("8095550101");
        var jose = await Identificado("8295550102");
        var ajena = await Herramienta(jose, "confirmar_asistencia", $$"""{"cita_id":"{{cita}}"}""");
        var propia = await Herramienta(maria, "confirmar_asistencia", $$"""{"cita_id":"{{cita}}"}""");

        Assert.Contains("\"error\"", ajena);
        Assert.Contains("\"asistencia_confirmada\":true", propia);
        Assert.NotNull((await _db.Contexto().Citas.SingleAsync()).AsistenciaConfirmadaUtc);
    }

    [Fact]
    public async Task El_despachador_reintenta_y_marca_fallido_al_agotar_intentos()
    {
        await CitaDeMariaMartes9();
        _reloj.Avanzar(TimeSpan.FromHours(7));
        await Peticion().Motor.ProcesarAsync(CancellationToken.None);
        _whatsApp.Fallar = true;

        for (var i = 0; i < 5; i++)
            await Peticion().Despachador.DespacharPendientesAsync(CancellationToken.None);

        var m = await _db.Contexto().MensajesSalientes.SingleAsync();
        Assert.Equal("fallido", m.Estado);
        Assert.Equal(5, m.Intentos);
        Assert.Contains("WhatsApp no respondió", m.Error);
    }

    [Fact]
    public async Task Sin_servicio_de_agenda_las_herramientas_lo_dicen_sin_romper()
    {
        var tools = new AgentTools(new MockDpmGateway(_reloj));
        var sesion = new Sesion("s");
        sesion.VincularTelefono("8095550101");
        await tools.ExecuteAsync(sesion, "buscar_paciente", "{}", CancellationToken.None);

        var r = await tools.ExecuteAsync(sesion, "responder_oferta", """{"acepta":true}""", CancellationToken.None);

        Assert.Contains("no está disponible", r);
    }
}
