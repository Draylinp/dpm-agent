using DpmAgent.Api.Agent;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MetaOptions>(builder.Configuration.GetSection(MetaOptions.SectionName));
builder.Services.AddHttpClient<IChatModel, MetaModelClient>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IDpmGateway, MockDpmGateway>();
builder.Services.AddSingleton<AgentTools>();
builder.Services.AddSingleton<ConversationStore>();
builder.Services.AddScoped<ReceptionAgent>();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { estado = "ok", hora = DateTimeOffset.UtcNow }));

app.MapPost("/api/chat", async (ChatRequest req, ReceptionAgent agent, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Mensaje))
        return Results.BadRequest(new { error = "El mensaje está vacío." });

    if (req.Mensaje.Length > 2000)
        return Results.BadRequest(new { error = "El mensaje es demasiado largo." });

    try
    {
        var reply = await agent.RunAsync(req.SesionId, req.Mensaje, req.Telefono, ct);
        return Results.Ok(reply);
    }
    catch (SesionInvalidaException ex)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("clave", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem($"El modelo no respondió: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/api/escalamientos", (AgentTools tools) =>
    Results.Ok(tools.Escalamientos.OrderByDescending(e => e.Creado)));

app.MapGet("/api/medicos", async (IDpmGateway dpm, CancellationToken ct) =>
    Results.Ok(await dpm.ListarMedicosAsync(ct)));

app.Run();

/// <param name="Telefono">
/// En el panel de prueba simula el número de WhatsApp del paciente. En WhatsApp lo pone el webhook.
/// </param>
public sealed record ChatRequest(string? SesionId, string Mensaje, string? Telefono = null);

public partial class Program;
