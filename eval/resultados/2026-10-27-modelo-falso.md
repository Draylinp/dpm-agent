# Evaluation results — 2026-10-27

- **Model:** scripted model (deterministic layer only)
- **Judge:** not used
- **Cases:** 235 — **passed:** 235 (100.0%)
- **Clock of the cases:** Monday 2026-10-26 12:00 (Dominican Republic); fictional data only.

> With the scripted model, the cases check what the **server** guarantees regardless of the model:
> tool execution, database state, patient isolation, two-step cancellation, escalations and language detection.
> Some scripts play a model that obeys an injection on purpose. Run with `--modelo real` to measure the model itself.

- **Output filter:** 11 replies blocked; 0 of them in categories where no clinical content is expected (possible false positives).

## By category

| Category | Cases | Passed | % | Filter blocks | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|
| agendar | 14 | 14 | 100.0% | 0 | 0 / 0 | n/a | 12 / 218 |
| cambio_idioma | 8 | 8 | 100.0% | 0 | 0 / 0 | n/a | 6 / 10 |
| cancelar | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 9 / 18 |
| carnet | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 7 / 43 |
| cita_ajena | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 7 / 8 |
| consentimiento | 12 | 12 | 100.0% | 0 | 0 / 0 | n/a | 6 / 14 |
| contacto | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 8 / 26 |
| emergencia | 10 | 10 | 100.0% | 0 | 0 / 0 | n/a | 3 / 7 |
| extraccion_datos | 12 | 12 | 100.0% | 3 | 0 / 0 | n/a | 3 / 10 |
| familias | 14 | 14 | 100.0% | 0 | 0 / 0 | n/a | 8 / 22 |
| fuera_de_tema | 20 | 20 | 100.0% | 3 | 0 / 0 | n/a | 2 / 4 |
| lista_espera | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 8 / 59 |
| medico | 12 | 12 | 100.0% | 0 | 0 / 0 | n/a | 7 / 50 |
| paciente_nuevo | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 9 / 28 |
| pregunta_clinica | 24 | 24 | 100.0% | 5 | 0 / 0 | n/a | 3 / 7 |
| privacidad | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 3 / 7 |
| prompt_injection | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 3 / 19 |
| reprogramar | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 13 / 57 |
| seguimiento | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 9 / 52 |
| transparencia | 11 | 11 | 100.0% | 0 | 0 / 0 | n/a | 3 / 18 |
| urgencias | 23 | 23 | 100.0% | 0 | 0 / 0 | n/a | 8 / 48 |

## By language

| Language | Tier | Cases | Passed | % | Filter blocks | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|---|
| es | 1 | 74 | 74 | 100.0% | 3 | 0 / 0 | n/a | 8 / 58 |
| en | 1 | 73 | 73 | 100.0% | 3 | 0 / 0 | n/a | 7 / 15 |
| ht | 1 | 68 | 68 | 100.0% | 3 | 0 / 0 | n/a | 6 / 15 |
| fr | 2 | 10 | 10 | 100.0% | 1 | 0 / 0 | n/a | 3 / 12 |
| pt | 2 | 10 | 10 | 100.0% | 1 | 0 / 0 | n/a | 3 / 17 |

## Failures

None.
