using System.Net.Http.Headers;
using System.Text;
using DpmAgent.Api.Data;
using DpmAgent.Api.Voz;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DpmAgent.Tests;

/// <summary>F31: cifrado del teléfono y el afiliado, TLS hacia PostgreSQL, llaves persistentes y archivos solo en memoria.</summary>
public class DatosEnReposoTests
{
    private static async Task<List<(string? Telefono, string? Afiliado, string? Hash)>> CrudosAsync(DpmDbContext db)
    {
        var conexion = db.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
            await conexion.OpenAsync();
        await using var cmd = conexion.CreateCommand();
        cmd.CommandText = "SELECT Telefono, NumeroAfiliado, TelefonoHash FROM Pacientes ORDER BY Id";
        await using var r = await cmd.ExecuteReaderAsync();
        var filas = new List<(string?, string?, string?)>();
        while (await r.ReadAsync())
            filas.Add((r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return filas;
    }

    [Fact]
    public async Task El_telefono_y_el_afiliado_se_guardan_cifrados_y_se_puede_buscar_por_telefono()
    {
        using var app = new AppDePrueba();
        var p = await app.ConGateway(g => g.RegistrarPacienteAsync("Rosa Peña (ficticia)", "(849) 555-0123", null, CancellationToken.None));
        await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            var rosa = await db.Pacientes.SingleAsync(x => x.Nombre.StartsWith("Rosa"));
            rosa.NumeroAfiliado = "AF-998877";
            return await db.SaveChangesAsync();
        });

        var crudos = await app.EnAmbito(sp => CrudosAsync(sp.GetRequiredService<DpmDbContext>()));
        Assert.Equal(3, crudos.Count);
        Assert.All(crudos, f =>
        {
            Assert.StartsWith(CifradoCampos.Prefijo, f.Telefono);
            Assert.DoesNotContain("555", f.Telefono);
            Assert.Matches("^[0-9a-f]{64}$", f.Hash);
        });
        Assert.StartsWith(CifradoCampos.Prefijo, crudos[^1].Afiliado);
        Assert.DoesNotContain("998877", crudos[^1].Afiliado);
        Assert.DoesNotContain("8495550123", crudos[^1].Hash);

        // Ida y vuelta: la aplicación lo ve en claro, y lo encuentra con el número escrito de cualquier forma.
        var leida = await app.EnAmbito(sp => sp.GetRequiredService<DpmDbContext>().Pacientes.AsNoTracking().SingleAsync(x => x.Nombre.StartsWith("Rosa")));
        Assert.Equal(("8495550123", "AF-998877"), (leida.Telefono, leida.NumeroAfiliado));
        foreach (var forma in new[] { "8495550123", "+1 849-555-0123", "(849) 555 0123" })
            Assert.Equal(p.Id, (await app.ConGateway(g => g.BuscarPacienteAsync(forma, CancellationToken.None)))?.Id);
        Assert.Null(await app.ConGateway(g => g.BuscarPacienteAsync("8495550124", CancellationToken.None)));
    }

    [Fact]
    public async Task Los_pacientes_guardados_antes_del_cifrado_se_cifran_al_arrancar()
    {
        using var sqlite = new SqliteDePrueba(new FixedClock(DateTimeOffset.UtcNow));
        var db = sqlite.Contexto();
        // Como quedaban antes de esta versión: en claro y sin HMAC.
        await db.Database.ExecuteSqlRawAsync("UPDATE Pacientes SET Telefono = '8095550101', NumeroAfiliado = 'AF-1', TelefonoHash = NULL WHERE Id = 1");
        Assert.Equal("8095550101", (await CrudosAsync(db))[0].Telefono);

        Assert.Equal(1, await CifradoCampos.CifrarExistentesAsync(db, CancellationToken.None));
        var crudo = (await CrudosAsync(db))[0];
        Assert.StartsWith(CifradoCampos.Prefijo, crudo.Telefono);
        Assert.StartsWith(CifradoCampos.Prefijo, crudo.Afiliado);
        Assert.NotNull(crudo.Hash);
        Assert.Equal(0, await CifradoCampos.CifrarExistentesAsync(db, CancellationToken.None)); // no se repite

        var otra = sqlite.Contexto();
        var hash = otra.HashTelefono("809-555-0101");
        Assert.Equal("AF-1", (await otra.Pacientes.AsNoTracking().SingleAsync(x => x.TelefonoHash == hash)).NumeroAfiliado);
    }

    [Theory]
    [InlineData("Host=db;Database=dpm;Username=u;Password=p", false)]
    [InlineData("Host=db;Database=dpm;Username=u;Password=p;SSL Mode=Disable", false)]
    [InlineData("Host=db;Database=dpm;Username=u;Password=p;SSL Mode=Prefer", false)]
    [InlineData("Host=db;Database=dpm;Username=u;Password=p;SSL Mode=Require", true)]
    [InlineData("Host=db;Database=dpm;Username=u;Password=p;Ssl Mode=VerifyFull", true)]
    public void En_produccion_postgres_exige_tls(string conexion, bool valida)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseDatos:Proveedor"] = "Postgres",
            ["ConnectionStrings:Dpm"] = conexion,
        }).Build();
        void Registrar() => new ServiceCollection().AddPersistencia(config, produccion: true);
        if (valida)
            Registrar();
        else
            Assert.Contains("TLS", Assert.Throws<InvalidOperationException>(Registrar).Message);

        // Fuera de producción (desarrollo, pruebas) no se exige.
        new ServiceCollection().AddPersistencia(config, produccion: false);
    }

    [Fact]
    public void En_produccion_no_arranca_sin_llaves_de_data_protection_persistentes()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("BaseDatos:Proveedor", "Memoria");
            b.UseSetting("DataProtection:Ruta", "");
        });
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("DataProtection__Ruta", ex.ToString());
    }

    /// <summary>Transcriptor que siempre falla: el archivo tampoco debe quedar en ninguna parte.</summary>
    private sealed class TranscriptorQueFalla : ITranscriber
    {
        public Task<Transcripcion> TranscribirAsync(AudioRecibido audio, CancellationToken ct) => throw new HttpRequestException("servicio caído");
    }

    [Fact]
    public async Task El_audio_y_la_imagen_se_procesan_en_memoria_y_no_quedan_guardados()
    {
        using var app = new AppDePrueba(servicios: s =>
        {
            s.RemoveAll<ITranscriber>();
            s.AddSingleton<ITranscriber, TranscriptorQueFalla>();
        });
        const string marca = "MARCA-ARCHIVO-PACIENTE-7731";
        var relleno = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat(marca, 12_000))); // ~320 KB: más que el umbral de 64 KB
        var temporales = Path.GetTempPath();
        var antes = Directory.GetFiles(temporales, "ASPNETCORE_*").ToHashSet();

        foreach (var (ruta, campo, tipo, datos) in new[]
                 {
                     ("/api/chat/audio", "audio", "audio/ogg", (byte[])[.. AudiosDePrueba.OggOpus(5), .. relleno]),
                     ("/api/pacientes/carnet", "imagen", "image/jpeg", (byte[])[0xFF, 0xD8, 0xFF, 0xE0, .. relleno]),
                 })
        {
            var archivo = new ByteArrayContent(datos);
            archivo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
            var form = new MultipartFormDataContent { { archivo, campo, "archivo" }, { new StringContent("809-555-0101"), "telefono" } };
            var r = await app.Cliente.PostAsync(ruta, form);
            Assert.True((int)r.StatusCode < 500 || r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || r.StatusCode == System.Net.HttpStatusCode.BadGateway,
                $"{ruta}: {r.StatusCode}");
        }

        // Ni en temporales de ASP.NET (también con error) ni en ninguna tabla de la base.
        Assert.Empty(Directory.GetFiles(temporales, "ASPNETCORE_*").Where(f => !antes.Contains(f)));
        var textos = await app.EnAmbito(async sp =>
        {
            var db = sp.GetRequiredService<DpmDbContext>();
            var conexion = db.Database.GetDbConnection();
            await conexion.OpenAsync();
            var tablas = new List<string>();
            await using (var cmd = conexion.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    tablas.Add(r.GetString(0));
            }

            var todo = new StringBuilder();
            foreach (var t in tablas)
            {
                await using var cmd = conexion.CreateCommand();
                cmd.CommandText = $"SELECT * FROM \"{t}\"";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    for (var i = 0; i < r.FieldCount; i++)
                        todo.Append(r.IsDBNull(i) ? "" : r.GetValue(i) is byte[] b ? Encoding.ASCII.GetString(b) : r.GetValue(i).ToString()).Append('|');
                }
            }

            return todo.ToString();
        });
        Assert.DoesNotContain(marca, textos);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.ASCII.GetBytes(marca + marca)), textos);
    }
}
