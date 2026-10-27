using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DpmAgent.Api.Carnet;
using DpmAgent.Api.Dpm;
using DpmAgent.Api.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

/// <summary>POST /api/pacientes/carnet y acciones de la secretaria, con lector y modelo falsos.</summary>
public class CarnetEndpointTests
{
    private static readonly LecturaCarnet CarnetDeMaria =
        new(true, new DatosCarnet("María Rodríguez", "ARS Demo Salud", "AF-777"));

    private sealed record Contexto(HttpClient Cliente, ScriptedChatModel Modelo, FakeLectorCarnet Lector, GatewayDeApp Dpm, AppDePrueba App);

    /// <summary>Acceso al gateway de la app: cada llamada usa un ámbito nuevo.</summary>
    private sealed class GatewayDeApp(AppDePrueba app)
    {
        public Task<IReadOnlyList<CarnetLeido>> ListarCarnetsPendientesAsync(CancellationToken ct) =>
            app.ConGateway(g => g.ListarCarnetsPendientesAsync(ct));

        public Task<Paciente?> BuscarPacienteAsync(string clave, CancellationToken ct) =>
            app.ConGateway(g => g.BuscarPacienteAsync(clave, ct));
    }

    private static Contexto Crear(LecturaCarnet lectura, ScriptedChatModel? modelo = null, long maxBytes = 4 * 1024 * 1024)
    {
        var lector = new FakeLectorCarnet(lectura);
        var app = new AppDePrueba(
            modelo,
            s =>
            {
                s.RemoveAll<ILectorCarnet>();
                s.AddSingleton<ILectorCarnet>(lector);
            },
            new Dictionary<string, string?> { ["Carnet:MaxBytes"] = maxBytes.ToString() });
        return new Contexto(app.Cliente, app.Modelo, lector, new GatewayDeApp(app), app);
    }

    private static MultipartFormDataContent Form(byte[] imagen, string? telefono = "809-555-0101", string? sesionId = null)
    {
        var archivo = new ByteArrayContent(imagen);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var form = new MultipartFormDataContent { { archivo, "imagen", "carnet.jpg" } };
        if (telefono is not null)
            form.Add(new StringContent(telefono), "telefono");
        if (sesionId is not null)
            form.Add(new StringContent(sesionId), "sesionId");
        return form;
    }

    [Fact]
    public async Task La_foto_queda_pendiente_y_el_agente_pide_confirmacion_con_los_datos()
    {
        var modelo = new ScriptedChatModel()
            .ThenText("Leí: María Rodríguez, ARS Demo Salud, afiliado AF-777. ¿Está correcto?");
        var (cliente, _, _, dpm, _) = Crear(CarnetDeMaria, modelo);

        var res = await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg()));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = (await res.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("pendiente", json["carnet"]!["estado"]!.ToString());

        var mensaje = modelo.Llamadas[0][^1]["content"]!.ToString();
        Assert.StartsWith("[Foto de carnet] ", mensaje);
        Assert.Contains("\"numero_afiliado\":\"AF-777\"", mensaje);

        var pendiente = Assert.Single(await dpm.ListarCarnetsPendientesAsync(CancellationToken.None));
        Assert.Equal("8095550101", pendiente.TelefonoCanal);
        Assert.Equal("AF-777", pendiente.Datos.NumeroAfiliado);
        // Nada se aplica al paciente hasta que alguien confirme.
        Assert.Null((await dpm.BuscarPacienteAsync("PAC-001", CancellationToken.None))!.NumeroAfiliado);
    }

    [Fact]
    public async Task Flujo_completo_foto_y_luego_el_paciente_confirma_en_el_chat()
    {
        var modelo = new ScriptedChatModel()
            .ThenText("¿Están correctos estos datos?")
            .ThenTool("confirmar_datos_carnet", new { })
            .ThenText("Listo, guardé su ARS.");
        var (cliente, _, _, dpm, _) = Crear(CarnetDeMaria, modelo);

        var r1 = await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg()));
        var sesionId = (await r1.Content.ReadFromJsonAsync<JsonObject>())!["sesionId"]!.ToString();
        var r2 = await cliente.PostAsJsonAsync("/api/chat", new { sesionId, mensaje = "Sí, están bien" });

        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        var maria = await dpm.BuscarPacienteAsync("PAC-001", CancellationToken.None);
        Assert.Equal("AF-777", maria!.NumeroAfiliado);
        Assert.Equal("ARS Demo Salud", maria.Ars);
        Assert.Empty(await dpm.ListarCarnetsPendientesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Si_no_es_un_carnet_no_queda_nada_pendiente()
    {
        var modelo = new ScriptedChatModel().ThenText("No pude leer el carnet. ¿Me manda otra foto?");
        var (cliente, _, _, dpm, _) = Crear(new LecturaCarnet(false, new DatosCarnet(null, null, null)), modelo);

        var res = await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg()));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Null((await res.Content.ReadFromJsonAsync<JsonObject>())!["carnet"]);
        Assert.Contains("no parece un carnet", modelo.Llamadas[0][^1]["content"]!.ToString());
        Assert.Empty(await dpm.ListarCarnetsPendientesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Sin_telefono_no_se_lee_la_foto()
    {
        var (cliente, _, lector, _, _) = Crear(CarnetDeMaria);

        var res = await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg(), telefono: null));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(0, lector.Lecturas);
    }

    [Fact]
    public async Task Archivo_que_no_es_imagen_o_demasiado_grande_se_rechaza_sin_leer()
    {
        var (cliente, _, lector, _, _) = Crear(CarnetDeMaria, maxBytes: 1000);

        var noImagen = await cliente.PostAsync("/api/pacientes/carnet", Form("%PDF-1.7 carnet"u8.ToArray()));
        var grande = await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg(5000)));

        Assert.Equal(HttpStatusCode.BadRequest, noImagen.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        Assert.Equal(0, lector.Lecturas);
    }

    [Fact]
    public async Task La_secretaria_corrige_confirma_y_no_puede_confirmar_dos_veces()
    {
        var (cliente, _, _, dpm, app) = Crear(CarnetDeMaria, new ScriptedChatModel().ThenText("¿Correcto?"));
        await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg()));
        await app.IniciarSesionAsync();
        var id = (await cliente.GetFromJsonAsync<JsonArray>("/api/carnets"))![0]!["id"]!.ToString();

        var malo = await cliente.PostAsJsonAsync($"/api/carnets/{id}/confirmar", new { numeroAfiliado = "AF 1; DROP" });
        var bueno = await cliente.PostAsJsonAsync($"/api/carnets/{id}/confirmar", new { numeroAfiliado = "AF-778" });
        var otraVez = await cliente.PostAsJsonAsync($"/api/carnets/{id}/confirmar", new { });

        Assert.Equal(HttpStatusCode.BadRequest, malo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bueno.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, otraVez.StatusCode);
        Assert.Equal("AF-778", (await dpm.BuscarPacienteAsync("PAC-001", CancellationToken.None))!.NumeroAfiliado);
    }

    [Fact]
    public async Task La_secretaria_puede_descartar_y_un_id_inexistente_da_404()
    {
        var (cliente, _, _, dpm, app) = Crear(CarnetDeMaria, new ScriptedChatModel().ThenText("¿Correcto?"));
        await cliente.PostAsync("/api/pacientes/carnet", Form(ImagenesDePrueba.Jpeg()));
        await app.IniciarSesionAsync();
        var id = (await cliente.GetFromJsonAsync<JsonArray>("/api/carnets"))![0]!["id"]!.ToString();

        var descartar = await cliente.PostAsync($"/api/carnets/{id}/descartar", null);
        var noExiste = await cliente.PostAsync("/api/carnets/CAR-999/descartar", null);

        Assert.Equal(HttpStatusCode.OK, descartar.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, noExiste.StatusCode);
        Assert.Empty(await dpm.ListarCarnetsPendientesAsync(CancellationToken.None));
        Assert.Null((await dpm.BuscarPacienteAsync("PAC-001", CancellationToken.None))!.NumeroAfiliado);
    }
}
