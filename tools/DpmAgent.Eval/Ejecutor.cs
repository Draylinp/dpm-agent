using System.Diagnostics;
using System.Globalization;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Medicos;
using DpmAgent.Api.Model;
using DpmAgent.Api.Seguridad;
using DpmAgent.Api.Voz;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DpmAgent.Eval;

public sealed record ResultadoTurno(string Entrada, string Respuesta, IReadOnlyList<AgentStep> Pasos, string? Idioma, int Milisegundos);

public sealed record ResultadoCaso(
    Caso Caso,
    bool Pasa,
    IReadOnlyList<string> Fallos,
    IReadOnlyList<ResultadoTurno> Turnos,
    IReadOnlyList<UsoModelo> Uso,
    decimal? Costo,
    ResultadoJuez? Juez,
    string? Error)
{
    public long TokensEntrada => Uso.Sum(u => (long)u.TokensEntrada);

    public long TokensSalida => Uso.Sum(u => (long)u.TokensSalida);

    /// <summary>Respuestas que el filtro de salida bloqueó.</summary>
    public int Bloqueos => Turnos.SelectMany(t => t.Pasos).Count(p => p.Herramienta == "filtro_salida");
}

/// <summary>
/// Corre un caso de punta a punta con los mismos servicios que la app: base SQLite en memoria con los
/// datos de demo, agente, herramientas, agenda, bitácora y procesadores de voz y carnet.
/// </summary>
public sealed class Ejecutor
{
    /// <summary>Lunes 26 de octubre de 2026, 12:00 p. m. en República Dominicana: el "ahora" de todos los casos.</summary>
    public static readonly DateTimeOffset Ahora = new(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4));

    private readonly IConfiguration _config;
    private readonly bool _modeloReal;
    private readonly IJuez? _juez;

    public Ejecutor(IConfiguration config, bool modeloReal, IJuez? juez = null)
    {
        _config = config;
        _modeloReal = modeloReal;
        _juez = juez;
    }

    public async Task<ResultadoCaso> EjecutarAsync(Caso caso, CancellationToken ct)
    {
        var uso = new RegistroUsoEnMemoria();
        var entrada = new EntradaSimulada();
        var reemplazos = new Dictionary<string, string>();
        string Reemplazar(string s) => reemplazos.Aggregate(s, (acc, r) => acc.Replace(r.Key, r.Value, StringComparison.Ordinal));

        await using var sp = Servicios(caso, uso, entrada, Reemplazar);
        var turnos = new List<ResultadoTurno>();

        try
        {
            await sp.InicializarBaseDatosAsync(ct);
            await PrepararAsync(sp, caso, reemplazos, ct);

            var telefono = Telefono.Normalizar(caso.Telefono)!;
            var sesionId = Api.Canales.CanalesConocidos.SesionWhatsApp(telefono);

            foreach (var turno in caso.Turnos)
            {
                await using var scope = sp.CreateAsyncScope();
                var reloj = Stopwatch.StartNew();
                turnos.Add(await TurnoAsync(scope.ServiceProvider, turno, sesionId, telefono, entrada, Reemplazar, ct) with
                {
                    Milisegundos = (int)reloj.ElapsedMilliseconds,
                });
            }

            var fallos = await Verificador.VerificarAsync(sp, caso, turnos, ct);
            var juez = _juez is null ? null : await _juez.CalificarAsync(caso, turnos, ct);
            if (juez is { ConsejoClinico: true })
                fallos.Add("El juez detectó consejo clínico en una respuesta.");

            return new ResultadoCaso(caso, fallos.Count == 0, fallos, turnos, uso.Usos, Costo(sp, uso), juez, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoCaso(caso, false, [$"Error: {ex.Message}"], turnos, uso.Usos, Costo(sp, uso), null, ex.Message);
        }
    }

    private ServiceProvider Servicios(Caso caso, RegistroUsoEnMemoria uso, EntradaSimulada entrada, Func<string, string> reemplazar)
    {
        var config = new ConfigurationBuilder()
            .AddConfiguration(_config)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BaseDatos:Proveedor"] = "Memoria",
                ["BaseDatos:SembrarDatosDemo"] = "true",
            })
            .AddInMemoryCollection(_modeloReal
                ? []
                // El modelo guionado no sabe clasificar: con él, el filtro de salida usa solo las reglas.
                : new Dictionary<string, string?> { ["FiltroSalida:Proveedor"] = "Reglas" })
            .Build();

        var s = new ServiceCollection();
        s.AddSingleton<IConfiguration>(config);
        s.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
        s.AddSingleton<TimeProvider>(new RelojFijo(Ahora));
        s.AddSingleton(uso);
        s.AddSingleton<IRegistroUso>(uso);
        s.AddSingleton<CalculadoraCosto>();
        s.Configure<PreciosOptions>(config.GetSection(PreciosOptions.SectionName));

        s.AddPersistencia(config);
        s.AddAgenda(config);
        s.Configure<TranscripcionOptions>(config.GetSection(TranscripcionOptions.SectionName));
        s.Configure<CarnetOptions>(config.GetSection(CarnetOptions.SectionName));
        s.AddSingleton<ITranscriber>(entrada);
        s.AddSingleton<ILectorCarnet>(entrada);
        s.AddScoped<ProcesadorNotaVoz>();
        s.AddScoped<ProcesadorCarnet>();
        s.AddCumplimiento(config);
        s.AddCanalMedico(config);
        s.AddScoped<AgentTools>();
        s.AddScoped<ReceptionAgent>();
        s.AddFiltroSalida(config);

        if (_modeloReal)
        {
            s.AddModelos(config);
        }
        else
        {
            // Un solo modelo guionado para todo el caso: el guion sigue entre turnos.
            var modelo = new ModeloGuionado(caso.Guion, reemplazar);
            s.AddSingleton<IChatModel>(modelo);
        }

        return s.BuildServiceProvider();
    }

    private static async Task PrepararAsync(IServiceProvider sp, Caso caso, Dictionary<string, string> reemplazos, CancellationToken ct)
    {
        if (caso.Preparacion is not { } prep)
            return;

        await using var scope = sp.CreateAsyncScope();
        var dpm = scope.ServiceProvider.GetRequiredService<IDpmGateway>();
        var db = scope.ServiceProvider.GetRequiredService<DpmDbContext>();

        if (prep.TelefonoEmergencias is { } emergencias)
        {
            var consultorio = await db.Consultorios.FirstAsync(c => c.Id == db.ConsultorioId, ct);
            consultorio.TelefonoEmergencias = emergencias;
            await db.SaveChangesAsync(ct);
        }

        if (prep.SinConsentimientos)
            await db.Consentimientos.ExecuteDeleteAsync(ct);

        foreach (var (medicoId, telefono) in prep.Medicos)
        {
            var m = await db.Medicos.FirstAsync(x => x.Id == IdsDpm.Leer(medicoId, "MED"), ct);
            m.Telefono = Telefono.Normalizar(telefono);
            m.TelefonoVerificadoUtc = Ahora.UtcDateTime;
            await db.SaveChangesAsync(ct);
        }

        foreach (var s in prep.Seguimientos)
        {
            db.Seguimientos.Add(new Api.Data.Entidades.Seguimiento
            {
                ConsultorioId = db.ConsultorioId,
                PacienteId = IdsDpm.Leer(s.Paciente, "PAC")!.Value,
                MedicoId = IdsDpm.Leer(s.Medico, "MED")!.Value,
                FechaObjetivo = DateOnly.ParseExact(s.FechaObjetivo, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ToleranciaDias = s.Tolerancia,
                Motivo = "control",
                Estado = s.Estado,
                Intentos = s.Estado == "contactando" ? 1 : 0,
                CreadoPor = "eval",
                CreadoUtc = Ahora.UtcDateTime,
            });
            await db.SaveChangesAsync(ct);
        }

        foreach (var (pacienteId, idioma) in prep.IdiomasPacientes)
        {
            var id = IdsDpm.Leer(pacienteId, "PAC");
            var p = await db.Pacientes.FirstAsync(x => x.Id == id, ct);
            p.Idioma = idioma;
            await db.SaveChangesAsync(ct);
        }

        var ids = new List<string>();
        foreach (var c in prep.Citas)
        {
            var fecha = DateOnly.ParseExact(c.Fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var hora = TimeOnly.ParseExact(c.Hora, "HH:mm", CultureInfo.InvariantCulture);
            var cita = await dpm.CrearCitaAsync(c.Paciente, c.Medico, HorarioConsultorio.EnHoraRd(fecha, hora), "control", ct);
            reemplazos[$"{{cita{ids.Count}}}"] = cita.Id;
            ids.Add(cita.Id);
        }

        var lista = scope.ServiceProvider.GetRequiredService<ServicioListaEspera>();
        foreach (var e in prep.ListaEspera)
        {
            await lista.AgregarAsync(e.Paciente, e.Medico,
                DateOnly.ParseExact(e.Desde, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(e.Hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(e.HoraDesde, "HH:mm", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(e.HoraHasta, "HH:mm", CultureInfo.InvariantCulture), ct);
        }

        if (prep.Cancelar.Count > 0)
        {
            foreach (var indice in prep.Cancelar)
                await dpm.CancelarCitaAsync(ids[indice], prep.Citas[indice].Paciente, ct);

            // El motor ofrece los huecos liberados a la lista de espera, como en producción.
            await using var motorScope = sp.CreateAsyncScope();
            await motorScope.ServiceProvider.GetRequiredService<MotorAgenda>().ProcesarAsync(ct);
        }
    }

    private static async Task<ResultadoTurno> TurnoAsync(
        IServiceProvider sp, Turno turno, string sesionId, string telefono, EntradaSimulada entrada, Func<string, string> reemplazar, CancellationToken ct)
    {
        try
        {
            if (turno.Voz is { } voz)
            {
                entrada.Transcripcion = reemplazar(voz);
                var r = await sp.GetRequiredService<ProcesadorNotaVoz>().ProcesarAsync(EntradaSimulada.Audio(), sesionId, telefono, ct);
                return new ResultadoTurno($"[voz] {entrada.Transcripcion}", r.Respuesta, r.Pasos, r.Idioma, 0);
            }

            if (turno.Foto is { } foto)
            {
                entrada.Foto = foto;
                var r = await sp.GetRequiredService<ProcesadorCarnet>().ProcesarAsync(EntradaSimulada.Imagen(), sesionId, telefono, ct);
                return new ResultadoTurno($"[foto] {foto.Nombre} / {foto.Ars} / {foto.Afiliado}", r.Respuesta, r.Pasos, r.Idioma, 0);
            }

            var texto = reemplazar(turno.Texto ?? "");
            var reply = await sp.GetRequiredService<ReceptionAgent>().RunAsync(sesionId, texto, telefono, ct);
            return new ResultadoTurno(texto, reply.Respuesta, reply.Pasos, reply.Idioma, 0);
        }
        catch (EntradaRechazadaException ex)
        {
            return new ResultadoTurno(turno.Texto ?? turno.Voz ?? "[foto]", ex.Message, [], null, 0);
        }
    }

    private static decimal? Costo(IServiceProvider sp, RegistroUsoEnMemoria uso)
    {
        // Sin llamadas reales (modelo falso) no hay costo que informar.
        if (uso.Usos.Count == 0)
            return null;

        var calculadora = sp.GetRequiredService<CalculadoraCosto>();
        decimal total = 0;
        foreach (var u in uso.Usos)
        {
            if (calculadora.Costo(u.Modelo, u.TokensEntrada, u.TokensSalida) is not { } costo)
                return null;
            total += costo;
        }

        return total;
    }
}

public sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => ahora.ToUniversalTime();
}
