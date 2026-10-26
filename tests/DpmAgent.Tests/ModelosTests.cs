using System.Net;
using System.Text.Json.Nodes;
using DpmAgent.Api.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Tests;

public class ModelosTests
{
    private sealed class Handler(HttpStatusCode status, string cuerpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Peticion { get; private set; }

        public JsonObject? Enviado { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Peticion = request;
            Enviado = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            return new HttpResponseMessage(status) { Content = new StringContent(cuerpo) };
        }
    }

    private const string RespuestaOk = """{"choices":[{"message":{"role":"assistant","content":"Hola"}}],"usage":{"total_tokens":12}}""";

    /// <summary>Modelo falso que responde fijo o lanza la excepción indicada.</summary>
    private sealed class ModeloFijo(Exception? error = null) : IChatModel
    {
        public int Llamadas { get; private set; }

        public Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
        {
            Llamadas++;
            return error is null
                ? Task.FromResult(new JsonObject { ["role"] = "assistant", ["content"] = "respaldo" })
                : Task.FromException<JsonObject>(error);
        }
    }

    private static readonly List<JsonObject> Mensajes = [new() { ["role"] = "user", ["content"] = "hola" }];

    [Fact]
    public async Task Servidor_propio_usa_max_tokens_y_solo_manda_clave_si_hay()
    {
        var conClave = new Handler(HttpStatusCode.OK, RespuestaOk);
        var sinClave = new Handler(HttpStatusCode.OK, RespuestaOk);
        IChatModel Crear(Handler h, string? clave) => new OpenAICompatibleChatModel(new HttpClient(h),
            Options.Create(new OpenAICompatibleOptions { BaseUrl = "http://glimmer.local/v1", Model = "muse-glimmer", ApiKey = clave }),
            NullLogger<OpenAICompatibleChatModel>.Instance);

        var r = await Crear(conClave, "secreto").CompleteAsync(Mensajes, [new JsonObject { ["type"] = "function" }], CancellationToken.None);
        await Crear(sinClave, null).CompleteAsync(Mensajes, [], CancellationToken.None);

        Assert.Equal("Hola", r["content"]!.ToString());
        Assert.Equal("http://glimmer.local/v1/chat/completions", conClave.Peticion!.RequestUri!.ToString());
        Assert.Equal("Bearer secreto", conClave.Peticion.Headers.Authorization!.ToString());
        Assert.Equal("muse-glimmer", conClave.Enviado!["model"]!.ToString());
        Assert.Equal(1024, conClave.Enviado["max_tokens"]!.GetValue<int>());
        Assert.Null(conClave.Enviado["max_completion_tokens"]);
        Assert.Equal("auto", conClave.Enviado["tool_choice"]!.ToString());
        Assert.Null(sinClave.Peticion!.Headers.Authorization);
        Assert.Null(sinClave.Enviado!["tools"]);
    }

    [Fact]
    public async Task Meta_usa_max_completion_tokens()
    {
        var h = new Handler(HttpStatusCode.OK, RespuestaOk);
        var meta = new MetaModelClient(new HttpClient(h),
            Options.Create(new MetaOptions { ApiKey = "k", BaseUrl = "https://api.ejemplo.test/v1/" }), NullLogger<MetaModelClient>.Instance);

        await meta.CompleteAsync(Mensajes, [], CancellationToken.None);

        Assert.Equal("muse-spark-1.2", h.Enviado!["model"]!.ToString());
        Assert.NotNull(h.Enviado["max_completion_tokens"]);
    }

    public static TheoryData<Exception, bool> Casos => new()
    {
        { new HttpRequestException("caído", null, HttpStatusCode.ServiceUnavailable), true },
        { new HttpRequestException("saturado", null, HttpStatusCode.TooManyRequests), true },
        { new HttpRequestException("sin red"), true },
        { new TaskCanceledException("tiempo de espera"), true },
        { new InvalidOperationException("Falta la clave"), true },
        { new HttpRequestException("petición mala", null, HttpStatusCode.BadRequest), false },
        { new HttpRequestException("no autorizado", null, HttpStatusCode.Unauthorized), false },
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task El_respaldo_solo_entra_si_el_principal_no_esta_disponible(Exception error, bool usaRespaldo)
    {
        var principal = new ModeloFijo(error);
        var respaldo = new ModeloFijo();
        var modelo = new ModeloConRespaldo(principal, respaldo, NullLogger<ModeloConRespaldo>.Instance);

        if (usaRespaldo)
        {
            var r = await modelo.CompleteAsync(Mensajes, [], CancellationToken.None);
            Assert.Equal("respaldo", r["content"]!.ToString());
        }
        else
        {
            await Assert.ThrowsAsync(error.GetType(), () => modelo.CompleteAsync(Mensajes, [], CancellationToken.None));
        }

        Assert.Equal(usaRespaldo ? 1 : 0, respaldo.Llamadas);
    }

    [Fact]
    public async Task Si_el_cliente_cancela_no_se_usa_el_respaldo()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var respaldo = new ModeloFijo();
        var modelo = new ModeloConRespaldo(new ModeloFijo(new OperationCanceledException(cts.Token)), respaldo, NullLogger<ModeloConRespaldo>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => modelo.CompleteAsync(Mensajes, [], cts.Token));
        Assert.Equal(0, respaldo.Llamadas);
    }

    private static IChatModel Resolver(Dictionary<string, string?> ajustes)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(ajustes).Build();
        var services = new ServiceCollection().AddLogging().AddModelos(config);
        return services.BuildServiceProvider().GetRequiredService<IChatModel>();
    }

    [Fact]
    public void La_configuracion_elige_proveedor_y_respaldo()
    {
        Assert.IsType<MetaModelClient>(Resolver([]));
        Assert.IsType<OpenAICompatibleChatModel>(Resolver(new()
        {
            ["Modelo:Proveedor"] = "OpenAICompatible",
            ["OpenAICompatible:Model"] = "muse-glimmer",
        }));
        Assert.IsType<ModeloConRespaldo>(Resolver(new()
        {
            ["Modelo:Proveedor"] = "MetaApi",
            ["Modelo:Respaldo"] = "OpenAICompatible",
            ["OpenAICompatible:Model"] = "muse-glimmer",
        }));
        Assert.Throws<InvalidOperationException>(() => Resolver(new() { ["Modelo:Proveedor"] = "OtroProveedor" }));
    }

    [Theory]
    [InlineData("Meta:Model", "muse-spark-1.2-contributor")]
    [InlineData("Carnet:Modelo", "muse-spark-1.3-contributor")]
    [InlineData("Transcripcion:Modelo", "muse-voice-transcribe-1.0-contributor")]
    public void No_se_permiten_variantes_contributor_con_datos_de_pacientes(string clave, string modelo)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolver(new() { [clave] = modelo }));
        Assert.Contains("contributor", ex.Message);
    }
}
