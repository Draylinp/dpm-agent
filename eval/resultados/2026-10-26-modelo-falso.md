# Evaluation results — 2026-10-26

- **Model:** scripted model (deterministic layer only)
- **Judge:** not used
- **Cases:** 84 — **passed:** 84 (100.0%)
- **Clock of the cases:** Monday 2026-10-26 12:00 (Dominican Republic); fictional data only.

> With the scripted model, the cases check what the **server** guarantees regardless of the model:
> tool execution, database state, patient isolation, two-step cancellation, escalations and language detection.
> Some scripts play a model that obeys an injection on purpose. Run with `--modelo real` to measure the model itself.

## By category

| Category | Cases | Passed | % | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|
| agendar | 8 | 8 | 100.0% | 0 / 0 | n/a | 14 / 451 |
| cambio_idioma | 8 | 8 | 100.0% | 0 / 0 | n/a | 6 / 10 |
| cancelar | 6 | 6 | 100.0% | 0 / 0 | n/a | 7 / 15 |
| carnet | 6 | 6 | 100.0% | 0 / 0 | n/a | 7 / 63 |
| cita_ajena | 6 | 6 | 100.0% | 0 / 0 | n/a | 7 / 11 |
| emergencia | 10 | 10 | 100.0% | 0 / 0 | n/a | 4 / 7 |
| lista_espera | 6 | 6 | 100.0% | 0 / 0 | n/a | 9 / 61 |
| paciente_nuevo | 6 | 6 | 100.0% | 0 / 0 | n/a | 10 / 17 |
| pregunta_clinica | 13 | 13 | 100.0% | 0 / 0 | n/a | 4 / 52 |
| prompt_injection | 9 | 9 | 100.0% | 0 / 0 | n/a | 5 / 16 |
| reprogramar | 6 | 6 | 100.0% | 0 / 0 | n/a | 13 / 37 |

## By language

| Language | Tier | Cases | Passed | % | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|
| es | 1 | 24 | 24 | 100.0% | 0 / 0 | n/a | 10 / 63 |
| en | 1 | 23 | 23 | 100.0% | 0 / 0 | n/a | 7 / 20 |
| ht | 1 | 23 | 23 | 100.0% | 0 / 0 | n/a | 7 / 14 |
| fr | 2 | 7 | 7 | 100.0% | 0 / 0 | n/a | 4 / 15 |
| pt | 2 | 7 | 7 | 100.0% | 0 / 0 | n/a | 4 / 13 |

## Failures

None.
