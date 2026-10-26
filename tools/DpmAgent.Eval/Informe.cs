using System.Globalization;
using System.Text;

namespace DpmAgent.Eval;

/// <summary>Tablas de resultados en Markdown: por categoría, por idioma y el detalle de lo que falló.</summary>
public static class Informe
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Generar(IReadOnlyList<ResultadoCaso> resultados, string modelo, bool modeloReal, string? juez, string moneda, DateTime fecha)
    {
        var sb = new StringBuilder();
        var aciertos = resultados.Count(r => r.Pasa);

        sb.AppendLine($"# Evaluation results — {fecha:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine($"- **Model:** {modelo}{(modeloReal ? "" : " (deterministic layer only)")}");
        sb.AppendLine($"- **Judge:** {juez ?? "not used"}");
        sb.AppendLine($"- **Cases:** {resultados.Count} — **passed:** {aciertos} ({Porcentaje(aciertos, resultados.Count)})");
        sb.AppendLine($"- **Clock of the cases:** Monday 2026-10-26 12:00 (Dominican Republic); fictional data only.");
        sb.AppendLine();
        if (!modeloReal)
        {
            sb.AppendLine("> With the scripted model, the cases check what the **server** guarantees regardless of the model:");
            sb.AppendLine("> tool execution, database state, patient isolation, two-step cancellation, escalations and language detection.");
            sb.AppendLine("> Some scripts play a model that obeys an injection on purpose. Run with `--modelo real` to measure the model itself.");
            sb.AppendLine();
        }

        sb.AppendLine("## By category");
        sb.AppendLine();
        Tabla(sb, resultados.GroupBy(r => r.Caso.Categoria).OrderBy(g => g.Key), "Category", moneda);

        sb.AppendLine("## By language");
        sb.AppendLine();
        Tabla(sb, resultados.GroupBy(r => r.Caso.Idioma).OrderBy(g => g.Key == "es" ? 0 : g.Key == "en" ? 1 : g.Key == "ht" ? 2 : 3).ThenBy(g => g.Key),
            "Language", moneda, nivel: true);

        var conJuez = resultados.Where(r => r.Juez is not null).ToList();
        if (conJuez.Count > 0)
        {
            sb.AppendLine("## LLM judge");
            sb.AppendLine();
            sb.AppendLine("| Tone (1-5) | Clarity (1-5) | Clinical advice detected | Correct language |");
            sb.AppendLine("|---|---|---|---|");
            sb.AppendLine($"| {conJuez.Average(r => r.Juez!.Tono).ToString("0.0", Inv)} | {conJuez.Average(r => r.Juez!.Claridad).ToString("0.0", Inv)} " +
                          $"| {conJuez.Count(r => r.Juez!.ConsejoClinico)} of {conJuez.Count} | {Porcentaje(conJuez.Count(r => r.Juez!.IdiomaCorrecto), conJuez.Count)} |");
            sb.AppendLine();
        }

        var fallidos = resultados.Where(r => !r.Pasa).ToList();
        sb.AppendLine("## Failures");
        sb.AppendLine();
        if (fallidos.Count == 0)
        {
            sb.AppendLine("None.");
        }
        else
        {
            sb.AppendLine("| Case | Reason |");
            sb.AppendLine("|---|---|");
            foreach (var r in fallidos)
                sb.AppendLine($"| `{r.Caso.Id}` | {string.Join("<br>", r.Fallos.Select(Escapar))} |");
        }

        return sb.ToString();
    }

    private static void Tabla(StringBuilder sb, IEnumerable<IGrouping<string, ResultadoCaso>> grupos, string titulo, string moneda, bool nivel = false)
    {
        sb.AppendLine($"| {titulo} |{(nivel ? " Tier |" : "")} Cases | Passed | % | Avg tokens in / out | Avg cost ({moneda}) | Latency p50 / p95 (ms) |");
        sb.AppendLine($"|---|{(nivel ? "---|" : "")}---|---|---|---|---|---|");
        foreach (var g in grupos)
        {
            var lista = g.ToList();
            var ok = lista.Count(r => r.Pasa);
            var latencias = lista.SelectMany(r => r.Turnos).Select(t => t.Milisegundos).Order().ToList();
            var costos = lista.Select(r => r.Costo).ToList();
            var costo = costos.All(c => c is not null) && costos.Count > 0
                ? (costos.Average(c => c!.Value)).ToString("0.00000", Inv)
                : "n/a";
            sb.AppendLine(
                $"| {g.Key} |{(nivel ? $" {Api.Idiomas.Idiomas.Nivel(g.Key)} |" : "")} {lista.Count} | {ok} | {Porcentaje(ok, lista.Count)} " +
                $"| {lista.Average(r => r.TokensEntrada).ToString("0", Inv)} / {lista.Average(r => r.TokensSalida).ToString("0", Inv)} " +
                $"| {costo} | {Percentil(latencias, 50)} / {Percentil(latencias, 95)} |");
        }

        sb.AppendLine();
    }

    internal static int Percentil(IReadOnlyList<int> ordenados, int p)
    {
        if (ordenados.Count == 0)
            return 0;
        var indice = (int)Math.Ceiling(p / 100.0 * ordenados.Count) - 1;
        return ordenados[Math.Clamp(indice, 0, ordenados.Count - 1)];
    }

    private static string Porcentaje(int a, int total) => total == 0 ? "—" : (100.0 * a / total).ToString("0.0", Inv) + "%";

    private static string Escapar(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
