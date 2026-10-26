using DpmAgent.Api.Agenda;
using DpmAgent.Api.Agent;
using DpmAgent.Api.Autorizaciones;
using DpmAgent.Api.Canales.WhatsApp;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Data;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;
using DpmAgent.Api.Panel;
using DpmAgent.Api.Voz;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MetaOptions>(builder.Configuration.GetSection(MetaOptions.SectionName));
builder.Services.AddHttpClient<IChatModel, MetaModelClient>();
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
builder.Services.AddScoped<AgentTools>();
builder.Services.AddScoped<ReceptionAgent>();

builder.Services.AddProblemDetails();

var app = builder.Build();

await app.Services.InicializarBaseDatosAsync();
await app.Services.AsegurarUsuarioInicialAsync(app.Environment.IsDevelopment());

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UsePanel();

app.MapGet("/health", () => Results.Ok(new { estado = "ok", hora = DateTimeOffset.UtcNow }));

app.MapPost("/api/chat", async (ChatRequest req, ReceptionAgent agent, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Mensaje))
        return Results.BadRequest(new { error = "El mensaje está vacío." });

    if (req.Mensaje.Length > 2000)
        return Results.BadRequest(new { error = "El mensaje es demasiado largo." });

    return await RespuestasAgente.EjecutarAsync(async () =>
        Results.Ok(await agent.RunAsync(req.SesionId, req.Mensaje, req.Telefono, ct)));
});

app.MapAudioChat();
app.MapCarnet();
app.MapAgenda();
app.MapWhatsApp();
app.MapPanel();
app.MapAutorizaciones();

app.MapGet("/api/escalamientos", async (IDpmGateway dpm, CancellationToken ct) =>
    Results.Ok(await dpm.ListarEscalamientosAsync(ct))).RequireAuthorization(PanelExtensions.Politica);

app.MapGet("/api/medicos", async (IDpmGateway dpm, CancellationToken ct) =>
    Results.Ok(await dpm.ListarMedicosAsync(ct)));

app.Run();

/// <param name="Telefono">
/// En el panel de prueba simula el número de WhatsApp del paciente. En WhatsApp lo pone el webhook.
/// </param>
public sealed record ChatRequest(string? SesionId, string Mensaje, string? Telefono = null);

public partial class Program;
