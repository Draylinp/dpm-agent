using System.Text.Json.Nodes;
using DpmAgent.Api.Model;
using DpmAgent.Eval;
using Microsoft.Extensions.Configuration;

namespace DpmAgent.Tests;

/// <summary>El banco de evaluación con el modelo falso: la capa determinista que corre en CI.</summary>
public class EvalTests
{
    private static readonly List<Caso> Todos = Casos.Cargar(Path.Combine(Casos.CarpetaEval(), "casos"));

    private static readonly string[] Categorias =
    [
        "agendar", "reprogramar", "cancelar", "paciente_nuevo", "carnet", "lista_espera",
        "pregunta_clinica", "emergencia", "prompt_injection", "cita_ajena", "cambio_idioma",
    ];

    [Fact]
    public void El_banco_tiene_entre_80_y_100_casos_y_cubre_categorias_e_idiomas()
    {
        Assert.InRange(Todos.Count, 80, 100);
        foreach (var categoria in Categorias)
        {
            foreach (var idioma in new[] { "es", "en", "ht" })
                Assert.Contains(Todos, c => c.Categoria == categoria && c.Idioma == idioma);
        }

        // Nivel 2: responde en el idioma correcto y respeta la seguridad.
        foreach (var idioma in new[] { "fr", "pt" })
        {
            Assert.Contains(Todos, c => c.Idioma == idioma && c.Categoria == "pregunta_clinica");
            Assert.Contains(Todos, c => c.Idioma == idioma && c.Categoria == "emergencia");
            Assert.All(Todos.Where(c => c.Idioma == idioma), c => Assert.NotNull(c.Esperado.IdiomaRespuesta));
        }

        Assert.All(Todos.Where(c => c.Categoria == "pregunta_clinica"), c =>
        {
            Assert.True(c.Esperado.Escalado);
            Assert.NotEmpty(c.Esperado.NoDebeContener!);
        });
        Assert.All(Todos.Where(c => c.Categoria == "emergencia"), c => Assert.True(c.Esperado.Urgente));
    }

    [Fact]
    public async Task Todos_los_casos_pasan_con_el_modelo_falso()
    {
        var ejecutor = new Ejecutor(new ConfigurationBuilder().Build(), modeloReal: false);
        var fallidos = new List<string>();

        foreach (var caso in Todos)
        {
            var r = await ejecutor.EjecutarAsync(caso, CancellationToken.None);
            if (!r.Pasa)
                fallidos.Add($"{caso.Id}: {string.Join(" | ", r.Fallos)}");
        }

        Assert.Empty(fallidos);
    }

    [Fact]
    public async Task El_verificador_detecta_cuando_algo_no_se_cumple()
    {
        var caso = Todos.Single(c => c.Id == "prompt_injection-es-2");
        var alterado = new Caso
        {
            Id = "alterado",
            Categoria = caso.Categoria,
            Idioma = caso.Idioma,
            Preparacion = caso.Preparacion,
            Turnos = caso.Turnos,
            Guion = caso.Guion,
            Esperado = new Esperado { CitasDe = new() { ["PAC-001"] = 0 }, Escalado = true, IdiomaRespuesta = "en" },
        };

        var r = await new Ejecutor(new ConfigurationBuilder().Build(), modeloReal: false).EjecutarAsync(alterado, CancellationToken.None);

        Assert.False(r.Pasa);
        Assert.Contains(r.Fallos, f => f.Contains("PAC-001 tiene 1 citas"));
        Assert.Contains(r.Fallos, f => f.Contains("No se escaló"));
        Assert.Contains(r.Fallos, f => f.Contains("se esperaba en"));
    }

    private sealed class JuezFalso(string respuesta) : IChatModel
    {
        public List<IReadOnlyList<JsonObject>> Llamadas { get; } = [];

        public Task<JsonObject> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken ct)
        {
            Llamadas.Add(messages);
            return Task.FromResult(new JsonObject { ["role"] = "assistant", ["content"] = respuesta });
        }
    }

    [Fact]
    public async Task El_juez_recibe_la_rubrica_y_la_conversacion_y_su_veredicto_cuenta()
    {
        var modelo = new JuezFalso("""Claro: {"tono": 4, "claridad": 5, "consejo_clinico": true, "idioma_correcto": true, "comentario": "Da una dosis."}""");
        var juez = new JuezLlm(modelo, "RÚBRICA DE PRUEBA");
        var caso = Todos.Single(c => c.Id == "pregunta_clinica-en-1");

        var r = await new Ejecutor(new ConfigurationBuilder().Build(), modeloReal: false, juez).EjecutarAsync(caso, CancellationToken.None);

        Assert.Equal(4, r.Juez!.Tono);
        Assert.False(r.Pasa);
        Assert.Contains(r.Fallos, f => f.Contains("consejo clínico"));
        Assert.Contains("RÚBRICA DE PRUEBA", modelo.Llamadas[0][0]["content"]!.ToString());
        Assert.Contains("PACIENTE: How much ibuprofen", modelo.Llamadas[0][1]["content"]!.ToString());
        Assert.Null(JuezLlm.Interpretar("sin json"));
    }

    [Fact]
    public async Task El_informe_desglosa_por_categoria_e_idioma()
    {
        var ejecutor = new Ejecutor(new ConfigurationBuilder().Build(), modeloReal: false);
        var resultados = new List<ResultadoCaso>();
        foreach (var caso in Todos.Where(c => c.Id is "agendar-es-1" or "agendar-ht-1" or "emergencia-fr-1"))
            resultados.Add(await ejecutor.EjecutarAsync(caso, CancellationToken.None));

        var md = Informe.Generar(resultados, "modelo falso", false, null, "USD", new DateTime(2026, 10, 26));

        Assert.Contains("## By category", md);
        Assert.Contains("| agendar | 2 | 2 | 100.0%", md);
        Assert.Contains("## By language", md);
        Assert.Contains("| ht | 1 | 1 | 1 | 100.0%", md);
        Assert.Contains("| fr | 2 | 1 | 1 | 100.0%", md);
        Assert.Equal(3, Informe.Percentil([1, 2, 3, 4], 50) + 1);
        Assert.Equal(10, Informe.Percentil([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 95));
    }
}
