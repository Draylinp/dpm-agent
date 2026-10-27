using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Autorizaciones;
using DpmAgent.Api.Canales.WhatsApp;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Costos;
using DpmAgent.Api.Cumplimiento;
using DpmAgent.Api.Data;
using DpmAgent.Api.Demo;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Medicos;
using DpmAgent.Api.Idiomas;
using DpmAgent.Api.Model;
using DpmAgent.Api.Panel;
using DpmAgent.Api.Seguridad;
using DpmAgent.Api.Voz;

var builder = WebApplication.CreateBuilder(args);

builder.AddProduccion();
builder.Services.AddModelos(builder.Configuration);
builder.Services.AddCostos(builder.Configuration);
builder.Services.Configure<TranscripcionOptions>(builder.Configuration.GetSection(TranscripcionOptions.SectionName));
builder.Services.AddHttpClient<ITranscriber, MetaTranscriber>();
builder.Services.Configure<CarnetOptions>(builder.Configuration.GetSection(CarnetOptions.SectionName));
builder.Services.AddHttpClient<ILectorCarnet, MetaLectorCarnet>();
builder.Services.AddScoped<ProcesadorNotaVoz>();
builder.Services.AddScoped<ProcesadorCarnet>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPersistencia(builder.Configuration);
builder.Services.AddAgenda(builder.Configuration);
builder.Services.AddWhatsApp(builder.Configuration);
builder.Services.AddPanel(builder.Configuration, builder.Environment);
builder.Services.AddScoped<ServicioAutorizaciones>();
builder.Services.AddCumplimiento(builder.Configuration);
builder.Services.AddFiltroSalida(builder.Configuration);
builder.Services.AddScoped<AgentTools>();
builder.Services.AddCanalMedico(builder.Configuration);
builder.Services.AddScoped<ReceptionAgent>();
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.SectionName));
builder.Services.AddScoped<ServicioDemo>();

builder.Services.AddProblemDetails();

var app = builder.Build();

await app.Services.InicializarBaseDatosAsync();
await app.Services.AsegurarUsuarioInicialAsync(app.Environment.IsDevelopment());
await app.Services.PrepararDemoAsync();

app.UseProduccion();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UsePanel();

app.MapSalud();

app.MapPost("/api/chat", async (ChatRequest req, HttpRequest http, ReceptionAgent agent, CancellationToken ct) =>
{
    var idioma = RespuestasAgente.IdiomaDe(http, req.Idioma);
    if (string.IsNullOrWhiteSpace(req.Mensaje))
        return Results.BadRequest(new { error = Textos.Obtener("entrada.mensaje_vacio", idioma) });

    if (req.Mensaje.Length > 2000)
        return Results.BadRequest(new { error = Textos.Obtener("entrada.mensaje_largo", idioma) });

    return await RespuestasAgente.EjecutarAsync(async () =>
        Results.Ok(await agent.RunAsync(req.SesionId, req.Mensaje, req.Telefono, ct)), idioma);
}).RequireRateLimiting(ProduccionExtensions.PoliticaPublica);

app.MapAudioChat();
app.MapCarnet();
app.MapAgenda();
app.MapWhatsApp();
app.MapPanel();
app.MapAutorizaciones();
app.MapDemo();
app.MapCostos();
app.MapCumplimiento();
app.MapSeguimientos();
app.MapMedicos();
app.MapUrgencias();
app.MapCambiosAgenda();

app.MapGet("/api/escalamientos", async (IDpmGateway dpm, CancellationToken ct) =>
    Results.Ok(await dpm.ListarEscalamientosAsync(ct))).RequireAuthorization(PanelExtensions.Politica);

app.MapGet("/api/medicos", async (IDpmGateway dpm, CancellationToken ct) =>
    Results.Ok(await dpm.ListarMedicosAsync(ct))).RequireRateLimiting(ProduccionExtensions.PoliticaPublica);

app.Run();

/// <param name="Telefono">
/// En el panel de prueba simula el número de WhatsApp del paciente. En WhatsApp lo pone el webhook.
/// </param>
/// <param name="Idioma">Idioma de la interfaz, para los mensajes de error.</param>
public sealed record ChatRequest(string? SesionId, string Mensaje, string? Telefono = null, string? Idioma = null);

public partial class Program;
