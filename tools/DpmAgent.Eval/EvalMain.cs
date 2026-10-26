using System.Globalization;
using DpmAgent.Api.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DpmAgent.Eval;

/// <summary>
/// Corre el banco de evaluación.
///   dotnet run --project tools/DpmAgent.Eval                         # modelo falso (CI): solo la capa determinista
///   dotnet run --project tools/DpmAgent.Eval -- --modelo real         # modelo configurado (Meta__*, Modelo__*) + juez
/// Opciones: --casos DIR, --salida DIR, --filtro TEXTO, --sin-juez, --no-escribir.
/// </summary>
public static class EvalMain
{
    public static async Task<int> Main(string[] args)
    {
        var opciones = Opciones.Leer(args);
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var eval = Casos.CarpetaEval();
        var casos = Casos.Cargar(opciones.Casos ?? Path.Combine(eval, "casos"))
            .Where(c => opciones.Filtro is null || c.Id.Contains(opciones.Filtro, StringComparison.OrdinalIgnoreCase))
            .ToList();

        IJuez? juez = null;
        string? nombreJuez = null;
        if (opciones.ModeloReal && !opciones.SinJuez && Juez(config) is { } j)
        {
            juez = j.Juez;
            nombreJuez = j.Nombre;
        }

        var ejecutor = new Ejecutor(config, opciones.ModeloReal, juez);
        var resultados = new List<ResultadoCaso>();
        foreach (var caso in casos)
        {
            var r = await ejecutor.EjecutarAsync(caso, CancellationToken.None);
            resultados.Add(r);
            Console.WriteLine($"{(r.Pasa ? "OK   " : "FALLA")} {caso.Id}{(r.Pasa ? "" : "  → " + string.Join(" | ", r.Fallos))}");
        }

        var modelo = opciones.ModeloReal ? config["Meta:Model"] ?? "muse-spark-1.2" : "scripted model";
        if (opciones.ModeloReal && string.Equals(config["Modelo:Proveedor"], "OpenAICompatible", StringComparison.OrdinalIgnoreCase))
            modelo = config["OpenAICompatible:Model"] ?? "OpenAI-compatible";

        var moneda = config["Precios:Moneda"] ?? "USD";
        var fecha = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Api.Dpm.HorarioConsultorio.ZonaRd).DateTime;
        var informe = Informe.Generar(resultados, modelo, opciones.ModeloReal, nombreJuez, moneda, fecha);

        if (!opciones.NoEscribir)
        {
            var salida = opciones.Salida ?? Path.Combine(eval, "resultados");
            Directory.CreateDirectory(salida);
            var archivo = Path.Combine(salida, $"{fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{(opciones.ModeloReal ? "" : "-modelo-falso")}.md");
            await File.WriteAllTextAsync(archivo, informe);
            Console.WriteLine($"\nInforme: {archivo}");
        }

        var aciertos = resultados.Count(r => r.Pasa);
        Console.WriteLine($"{aciertos}/{resultados.Count} casos pasan.");

        // Con el modelo falso todo debe pasar (es la red de seguridad del CI); con el real es una medición.
        return !opciones.ModeloReal && aciertos < resultados.Count ? 1 : 0;
    }

    private static (IJuez Juez, string Nombre)? Juez(IConfiguration config)
    {
        var s = config.GetSection("Eval:Juez");
        if (string.IsNullOrWhiteSpace(s["Model"]))
        {
            Console.WriteLine("Sin juez: defina Eval__Juez__Model (y BaseUrl/ApiKey) para la capa 2.");
            return null;
        }

        var opciones = Options.Create(new OpenAICompatibleOptions
        {
            BaseUrl = s["BaseUrl"] ?? "http://localhost:8000/v1/",
            Model = s["Model"]!,
            ApiKey = s["ApiKey"],
            Temperature = 0,
        });
        var modelo = new OpenAICompatibleChatModel(new HttpClient(), opciones, NullLogger<OpenAICompatibleChatModel>.Instance);
        var rubrica = File.ReadAllText(Path.Combine(Casos.CarpetaEval(), "RUBRICA.md"));
        return (new JuezLlm(modelo, rubrica), s["Model"]!);
    }

    private sealed record Opciones(bool ModeloReal, string? Casos, string? Salida, string? Filtro, bool SinJuez, bool NoEscribir)
    {
        public static Opciones Leer(string[] args)
        {
            string? Valor(string nombre)
            {
                var i = Array.IndexOf(args, nombre);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            return new Opciones(
                string.Equals(Valor("--modelo"), "real", StringComparison.OrdinalIgnoreCase),
                Valor("--casos"),
                Valor("--salida"),
                Valor("--filtro"),
                args.Contains("--sin-juez"),
                args.Contains("--no-escribir"));
        }
    }
}
