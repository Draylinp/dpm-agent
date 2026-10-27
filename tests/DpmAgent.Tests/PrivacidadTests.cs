using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.RegularExpressions;
using DpmAgent.Api.Agenda;
using DpmAgent.Api.Canales.WhatsApp;
using DpmAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Seguridad;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DpmAgent.Tests;

/// <summary>Guarda todo lo que se escribe en los logs (mensaje, excepción y valores de los scopes).</summary>
public sealed class CapturaLogs : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<string> Lineas { get; } = new();

    public ILogger CreateLogger(string categoria) => new Logger(this, categoria);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    // Identificadores técnicos de ASP.NET (no son datos de pacientes y pueden traer muchos dígitos).
    private static readonly HashSet<string> ClavesTecnicas = ["RequestId", "TraceId", "SpanId", "ParentId", "ConnectionId", "CorrelationId"];

    private sealed class Logger(CapturaLogs dueño, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => dueño._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel nivel, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatear)
        {
            var sb = new StringBuilder($"{nivel} {categoria}: {formatear(state, ex)}");
            if (ex is not null)
                sb.Append(" | ").Append(ex);
            dueño._scopes.ForEachScope((scope, b) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> pares)
                {
                    foreach (var (k, v) in pares)
                    {
                        if (!ClavesTecnicas.Contains(k) && k != "{OriginalFormat}")
                            b.Append(" | ").Append(k).Append('=').Append(v);
                    }
                }
            }, sb);
            dueño.Lineas.Enqueue(sb.ToString());
        }
    }
}

public class PrivacidadTests
{
    private const string Secreto = "secreto-de-prueba";

    // Teléfonos completos (con o sin código de país y separadores) y textos que escribió el paciente.
    private static readonly Regex Telefono = new(@"(?<!\d)(1?[\s-]?)?8[024]9[\s-]?\d{3}[\s-]?\d{4}(?!\d)");

    [Fact]
    public void Enmascara_numeros_y_correos()
    {
        var texto = Privacidad.Enmascarar("Error para +1 809-555-0101, cédula 001-1234567-8, correo maria@ejemplo.com, código 400");
        Assert.DoesNotMatch(Telefono, texto);
        Assert.DoesNotContain("1234567", texto);
        Assert.DoesNotContain("maria@", texto);
        Assert.Contains("código 400", texto);
    }

    [Fact]
    public void El_seudonimo_es_estable_y_no_contiene_el_numero()
    {
        Assert.Equal(Privacidad.Seudonimo("wa-8095550101"), Privacidad.Seudonimo("wa-8095550101"));
        Assert.NotEqual(Privacidad.Seudonimo("wa-8095550101"), Privacidad.Seudonimo("wa-8295550102"));
        Assert.DoesNotContain("555", Privacidad.Seudonimo("wa-8095550101"));
    }

    [Fact]
    public async Task Los_logs_de_un_flujo_completo_no_llevan_telefonos_ni_texto_de_pacientes()
    {
        const string textoPaciente = "Me llamo María y mi cédula es 001-1234567-8";
        var captura = new CapturaLogs();
        var reloj = new RelojAjustable(new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-4)));
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("escalar_a_personal", new { resumen_es = "Consulta administrativa", resumen_en = "Administrative question", urgente = false })
            .ThenText("Le paso con el personal.");
        for (var i = 0; i < 3; i++)
            modelo.Then(_ => throw new HttpRequestException("Meta no respondió para 18095550101"));

        var api = new WhatsAppApiFalsa();
        using var app = new AppDePrueba(modelo, s =>
        {
            s.AddSingleton<ILoggerProvider>(captura);
            s.RemoveAll<TimeProvider>();
            s.AddSingleton<TimeProvider>(reloj);
            s.RemoveAll<IWhatsAppCloudApi>();
            s.AddSingleton<IWhatsAppCloudApi>(api);
        }, new Dictionary<string, string?>
        {
            ["WhatsApp:AppSecret"] = Secreto,
            ["WhatsApp:PhoneNumberId"] = "PNID",
            ["WhatsApp:ProcesadorActivo"] = "false",
        });

        // Chat web con datos personales en el mensaje.
        (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = textoPaciente, telefono = "809-555-0101" })).EnsureSuccessStatusCode();

        // WhatsApp: el modelo falla tres veces (se registra la excepción) y se escala.
        var cuerpo = Encoding.UTF8.GetBytes(Payloads.Texto("PNID", "wamid.priv", "Hola, soy María"));
        var contenido = new ByteArrayContent(cuerpo);
        contenido.Headers.ContentType = new("application/json");
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/whatsapp/webhook") { Content = contenido };
        peticion.Headers.Add("X-Hub-Signature-256", WebhookWhatsApp.Firmar(cuerpo, Secreto));
        await app.Cliente.SendAsync(peticion);
        for (var i = 0; i < 3; i++)
        {
            await app.Services.GetRequiredService<ProcesadorWhatsApp>().ProcesarPendientesAsync(CancellationToken.None);
            await app.EnAmbito(sp => sp.GetRequiredService<DespachadorMensajes>().DespacharPendientesAsync(CancellationToken.None));
        }

        // Recordatorio automático.
        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01",
            HorarioConsultorio.EnHoraRd(new DateOnly(2026, 10, 27), new TimeOnly(9, 0)), "control", CancellationToken.None));
        reloj.Avanzar(TimeSpan.FromHours(7));
        await app.Services.GetRequiredService<AgendaHostedService>().VueltaAsync(CancellationToken.None);

        var logs = captura.Lineas.ToList();
        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.Contains("SesionId=s-"));
        Assert.All(logs, l =>
        {
            Assert.DoesNotMatch(Telefono, l);
            Assert.DoesNotContain("1234567", l);
            Assert.DoesNotContain("Me llamo", l);
            Assert.DoesNotContain("soy María", l);
        });
    }
}

public class DerechosDelPacienteTests
{
    private static async Task<AppDePrueba> ConHistoriaAsync()
    {
        var modelo = new ScriptedChatModel()
            .ThenTool("buscar_paciente", new { })
            .ThenTool("escalar_a_personal", new { resumen_es = "Quiere borrar sus datos", resumen_en = "Wants their data deleted", urgente = false })
            .ThenText("Le paso con la secretaria.")
            .ThenTool("buscar_paciente", new { })
            .ThenText("Hola, José.");
        var app = new AppDePrueba(modelo);
        (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Quiero que borren mis datos", telefono = "8095550101" })).EnsureSuccessStatusCode();
        (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola", telefono = "8295550102" })).EnsureSuccessStatusCode();
        await app.ConGateway(g => g.CrearCitaAsync("PAC-001", "MED-01",
            HorarioConsultorio.EnHoraRd(new DateOnly(2026, 11, 3), new TimeOnly(9, 0)), "control", CancellationToken.None));
        return app;
    }

    [Fact]
    public async Task La_secretaria_exporta_los_datos_del_paciente()
    {
        using var app = await ConHistoriaAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Cliente.GetAsync("/api/panel/pacientes/PAC-001/exportar")).StatusCode);

        await app.IniciarSesionAsync();
        var res = await app.Cliente.GetAsync("/api/panel/pacientes/PAC-001/exportar");
        var json = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;

        Assert.Equal("María Rodríguez (ficticia)", json["paciente"]!["nombre"]!.ToString());
        Assert.Single(json["citas"]!.AsArray());
        Assert.Equal(3, json["consentimientos"]!.AsArray().Count);
        var mensajes = json["conversaciones"]![0]!["mensajes"]!.AsArray();
        Assert.Equal("Quiero que borren mis datos", mensajes[0]!["texto"]!.ToString());
        Assert.Equal("paciente", mensajes[0]!["de"]!.ToString());
        Assert.Equal("Quiere borrar sus datos", json["escalamientos"]![0]!["motivo"]!.ToString());
        Assert.DoesNotContain("José", json.ToJsonString());

        var bitacora = (await app.Cliente.GetFromJsonAsync<JsonArray>("/api/bitacora"))!;
        Assert.Contains(bitacora, b => b!["accion"]!.ToString() == "datos_exportados");
    }

    [Fact]
    public async Task El_borrado_elimina_todo_lo_del_paciente_y_deja_la_bitacora_sin_datos_personales()
    {
        using var app = await ConHistoriaAsync();
        await app.IniciarSesionAsync();

        var sinConfirmar = await app.Cliente.PostAsJsonAsync("/api/panel/pacientes/PAC-001/borrar", new { confirmacion = "si" });
        Assert.Equal(HttpStatusCode.BadRequest, sinConfirmar.StatusCode);

        var res = await app.Cliente.PostAsJsonAsync("/api/panel/pacientes/PAC-001/borrar", new { confirmacion = "BORRAR" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var estado = await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            return new
            {
                Pacientes = await db.Pacientes.Select(p => p.Telefono).ToListAsync(),
                Conversaciones = await db.Conversaciones.Select(c => c.TelefonoCanal).ToListAsync(),
                Escalamientos = await db.Escalamientos.CountAsync(),
                Citas = await db.Citas.CountAsync(),
                Consentimientos = await db.Consentimientos.Select(c => c.PacienteId).Distinct().ToListAsync(),
                Bitacora = await db.Bitacora.ToListAsync(),
            };
        });

        Assert.Equal(["8295550102"], estado.Pacientes);
        Assert.Equal(["8295550102"], estado.Conversaciones);
        Assert.Equal(0, estado.Escalamientos);
        Assert.Equal(0, estado.Citas);
        Assert.Equal([2], estado.Consentimientos);
        Assert.Contains(estado.Bitacora, b => b.Accion == "datos_borrados" && b.EntidadId is null && b.Detalle is null);
        Assert.DoesNotContain(estado.Bitacora, b =>
            b.EntidadId == "PAC-001" || (b.Detalle ?? "").Contains("PAC-001") || b.TextoOriginal is not null && b.TextoOriginal.Contains("borren"));
        Assert.Contains(estado.Bitacora, b => b.Accion == "escalamiento" && b.SesionId is null);

        Assert.Equal(HttpStatusCode.NotFound, (await app.Cliente.GetAsync("/api/panel/pacientes/PAC-001/exportar")).StatusCode);
    }

    /// <summary>Datos de las fases 3 y 4: seguimientos, sobrecupos, familia, estado de contacto y consentimientos.</summary>
    private static async Task AgregarDatosNuevosAsync(AppDePrueba app)
    {
        await app.ConGateway(g => g.RegistrarPacienteAsync("Sofía Rodríguez (ficticia)", "8095550101", null, CancellationToken.None));
        await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            db.Seguimientos.Add(new Api.Data.Entidades.Seguimiento
            {
                ConsultorioId = 1, PacienteId = 1, MedicoId = 1, FechaObjetivo = new DateOnly(2027, 4, 26), ToleranciaDias = 7, Motivo = "control",
                Estado = "pendiente", CreadoPor = "prueba", CreadoUtc = DateTime.UtcNow,
            });
            db.SolicitudesSobrecupo.Add(new Api.Data.Entidades.SolicitudSobrecupo
            {
                ConsultorioId = 1, PacienteId = 1, MedicoId = 1, Fecha = new DateOnly(2026, 10, 27), SesionId = "s", Estado = "pendiente", CreadaUtc = DateTime.UtcNow,
            });
            db.Consentimientos.Add(new Api.Data.Entidades.Consentimiento
            {
                ConsultorioId = 1, PacienteId = 3, Telefono = "8095550101", Categoria = "recordatorios", Estado = "otorgado", Canal = "presencial",
                VersionTexto = "v", Idioma = "es", RegistradoPor = "prueba", FechaUtc = DateTime.UtcNow,
            });
            return await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task La_exportacion_incluye_seguimientos_sobrecupos_familia_y_contacto()
    {
        using var app = await ConHistoriaAsync();
        await AgregarDatosNuevosAsync(app);
        await app.IniciarSesionAsync();

        var json = JsonNode.Parse(await app.Cliente.GetStringAsync("/api/panel/pacientes/PAC-001/exportar"))!;
        Assert.Equal("2027-04-26", json["seguimientos"]![0]!["fechaObjetivo"]!.ToString());
        Assert.Equal("pendiente", json["solicitudesSobrecupo"]![0]!["estado"]!.ToString());
        Assert.Equal("Sofía Rodríguez (ficticia)", Assert.Single(json["vinculosFamiliares"]!.AsArray())!["nombre"]!.ToString());
        Assert.True(json["contacto"]!["activo"]!.GetValue<bool>());
        Assert.Equal(3, json["consentimientos"]!.AsArray().Count); // los de Sofía no van en la copia de María
    }

    [Fact]
    public async Task Borrar_a_un_miembro_de_la_familia_conserva_lo_del_numero_y_lo_de_los_demas()
    {
        using var app = await ConHistoriaAsync();
        await AgregarDatosNuevosAsync(app);
        await app.IniciarSesionAsync();

        var res = await app.Cliente.PostAsJsonAsync("/api/panel/pacientes/PAC-001/borrar", new { confirmacion = "BORRAR" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var estado = await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            return new
            {
                Pacientes = await db.Pacientes.Select(p => p.Id).OrderBy(i => i).ToListAsync(),
                Seguimientos = await db.Seguimientos.CountAsync(),
                Sobrecupos = await db.SolicitudesSobrecupo.CountAsync(),
                Consentimientos = await db.Consentimientos.Where(c => c.PacienteId == 1).CountAsync(),
                ConsentimientoSofia = await db.Consentimientos.CountAsync(c => c.PacienteId == 3),
            };
        });
        Assert.Equal([2, 3], estado.Pacientes);
        Assert.Equal((0, 0, 0, 1), (estado.Seguimientos, estado.Sobrecupos, estado.Consentimientos, estado.ConsentimientoSofia));
        var sofia = JsonNode.Parse(await app.Cliente.GetStringAsync("/api/panel/pacientes/PAC-003/exportar"))!;
        Assert.Empty(sofia["vinculosFamiliares"]!.AsArray());
    }
}

public class ErroresAmablesTests
{
    [Theory]
    [InlineData("es", "Disculpe, tuvimos un problema")]
    [InlineData("en", "Sorry, we had a problem answering you")]
    [InlineData("ht", "Eskize nou, nou te gen yon pwoblèm")]
    public async Task Si_el_modelo_falla_el_paciente_recibe_un_mensaje_amable_en_su_idioma(string idioma, string esperado)
    {
        var modelo = new ScriptedChatModel().Then(_ => throw new HttpRequestException("Meta respondió 500 para 18095550101"));
        using var app = new AppDePrueba(modelo);

        var res = await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola", telefono = "8095550101", idioma });
        var cuerpo = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Contains(esperado, cuerpo);
        Assert.DoesNotContain("Meta respondió", cuerpo);
        Assert.DoesNotContain("8095550101", cuerpo);
    }

    [Fact]
    public async Task Una_sesion_de_otro_numero_da_un_mensaje_amable_sin_detalles()
    {
        var modelo = new ScriptedChatModel().ThenText("Hola.");
        using var app = new AppDePrueba(modelo);
        var r1 = await (await app.Cliente.PostAsJsonAsync("/api/chat", new { mensaje = "Hola", telefono = "8095550101" })).Content.ReadFromJsonAsync<JsonObject>();

        var res = await app.Cliente.PostAsJsonAsync("/api/chat",
            new { sesionId = r1!["sesionId"]!.ToString(), mensaje = "Hello", telefono = "8295550102", idioma = "en" });

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("This conversation is no longer valid. Please start a new conversation.",
            (await res.Content.ReadFromJsonAsync<JsonObject>())!["error"]!.ToString());
    }

    [Fact]
    public async Task El_limite_de_uso_se_explica_en_el_idioma_de_la_interfaz()
    {
        using var app = new AppDePrueba(new ScriptedChatModel().ThenText("Hola.").ThenText("Hola."),
            ajustes: new Dictionary<string, string?> { ["Limites:PeticionesPorMinutoPorIp"] = "1" });

        var peticion = () =>
        {
            var m = new HttpRequestMessage(HttpMethod.Post, "/api/chat") { Content = JsonContent.Create(new { mensaje = "Mwen bezwen yon randevou" }) };
            m.Headers.AcceptLanguage.ParseAdd("ht");
            return app.Cliente.SendAsync(m);
        };
        await peticion();
        var res = await peticion();

        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.Contains("Ou voye anpil mesaj", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public void La_interfaz_tiene_los_mismos_textos_en_espanol_e_ingles()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DpmAgent.Api", "wwwroot", "i18n.js");
        var js = File.ReadAllText(ruta);
        var es = js[js.IndexOf("es: {", StringComparison.Ordinal)..js.IndexOf("    en: {", StringComparison.Ordinal)];
        var en = js[js.IndexOf("    en: {", StringComparison.Ordinal)..js.IndexOf("  };", StringComparison.Ordinal)];
        static List<string> Claves(string bloque) =>
            Regex.Matches(bloque, @"^\s+'([\w.]+)':", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        var claveEs = Claves(es);
        var claveEn = Claves(en);
        Assert.Equal(claveEs.Count, claveEs.Distinct().Count());
        Assert.Equal(claveEs.Order(), claveEn.Order());
    }
}
