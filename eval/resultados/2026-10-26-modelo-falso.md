# Evaluation results — 2026-10-26

- **Model:** scripted model (deterministic layer only)
- **Judge:** not used
- **Cases:** 177 — **passed:** 177 (100.0%)
- **Clock of the cases:** Monday 2026-10-26 12:00 (Dominican Republic); fictional data only.

> With the scripted model, the cases check what the **server** guarantees regardless of the model:
> tool execution, database state, patient isolation, two-step cancellation, escalations and language detection.
> Some scripts play a model that obeys an injection on purpose. Run with `--modelo real` to measure the model itself.

- **Output filter:** 8 replies blocked; 0 of them in categories where no clinical content is expected (possible false positives).

## By category

| Category | Cases | Passed | % | Filter blocks | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|
| agendar | 14 | 14 | 100.0% | 0 | 0 / 0 | n/a | 8 / 134 |
| cambio_idioma | 8 | 8 | 100.0% | 0 | 0 / 0 | n/a | 3 / 6 |
| cancelar | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 5 / 9 |
| carnet | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 4 / 30 |
| cita_ajena | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 4 / 5 |
| consentimiento | 12 | 12 | 100.0% | 0 | 0 / 0 | n/a | 4 / 10 |
| emergencia | 10 | 10 | 100.0% | 0 | 0 / 0 | n/a | 2 / 5 |
| fuera_de_tema | 20 | 20 | 100.0% | 3 | 0 / 0 | n/a | 1 / 2 |
| lista_espera | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 7 / 46 |
| medico | 12 | 12 | 100.0% | 0 | 0 / 0 | n/a | 4 / 22 |
| paciente_nuevo | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 7 / 13 |
| pregunta_clinica | 24 | 24 | 100.0% | 5 | 0 / 0 | n/a | 3 / 5 |
| privacidad | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 1 / 5 |
| prompt_injection | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 2 / 9 |
| reprogramar | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 9 / 27 |
| seguimiento | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 5 / 39 |
| transparencia | 11 | 11 | 100.0% | 0 | 0 / 0 | n/a | 2 / 7 |

## By language

| Language | Tier | Cases | Passed | % | Filter blocks | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|---|
| es | 1 | 53 | 53 | 100.0% | 2 | 0 / 0 | n/a | 5 / 39 |
| en | 1 | 52 | 52 | 100.0% | 2 | 0 / 0 | n/a | 4 / 11 |
| ht | 1 | 52 | 52 | 100.0% | 2 | 0 / 0 | n/a | 4 / 9 |
| fr | 2 | 10 | 10 | 100.0% | 1 | 0 / 0 | n/a | 2 / 8 |
| pt | 2 | 10 | 10 | 100.0% | 1 | 0 / 0 | n/a | 2 / 8 |

## Failures

None.
