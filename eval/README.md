# Evaluation bank

84 cases (`casos/`), the judge rubric (`RUBRICA.md`) and reports (`resultados/`). See [docs/EVALUATION.md](../docs/EVALUATION.md) · [docs/es/EVALUACION.md](../docs/es/EVALUACION.md).

```bash
dotnet run --project tools/DpmAgent.Eval                    # scripted model (CI)
dotnet run --project tools/DpmAgent.Eval -- --modelo real   # configured model + judge
```
