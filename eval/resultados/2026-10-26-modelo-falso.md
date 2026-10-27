# Evaluation results — 2026-10-26

- **Model:** scripted model (deterministic layer only)
- **Judge:** not used
- **Cases:** 200 — **passed:** 200 (100.0%)
- **Clock of the cases:** Monday 2026-10-26 12:00 (Dominican Republic); fictional data only.

> With the scripted model, the cases check what the **server** guarantees regardless of the model:
> tool execution, database state, patient isolation, two-step cancellation, escalations and language detection.
> Some scripts play a model that obeys an injection on purpose. Run with `--modelo real` to measure the model itself.

- **Output filter:** 8 replies blocked; 0 of them in categories where no clinical content is expected (possible false positives).

## By category

| Category | Cases | Passed | % | Filter blocks | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|
| agendar | 14 | 14 | 100.0% | 0 | 0 / 0 | n/a | 7 / 117 |
| cambio_idioma | 8 | 8 | 100.0% | 0 | 0 / 0 | n/a | 4 / 8 |
| cancelar | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 6 / 12 |
| carnet | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 5 / 42 |
| cita_ajena | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 5 / 5 |
| consentimiento | 12 | 12 | 100.0% | 0 | 0 / 0 | n/a | 4 / 10 |
| emergencia | 10 | 10 | 100.0% | 0 | 0 / 0 | n/a | 2 / 5 |
| fuera_de_tema | 20 | 20 | 100.0% | 3 | 0 / 0 | n/a | 1 / 2 |
| lista_espera | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 8 / 46 |
| medico | 12 | 12 | 100.0% | 0 | 0 / 0 | n/a | 4 / 21 |
| paciente_nuevo | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 8 / 17 |
| pregunta_clinica | 24 | 24 | 100.0% | 5 | 0 / 0 | n/a | 3 / 6 |
| privacidad | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 1 / 5 |
| prompt_injection | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 3 / 8 |
| reprogramar | 6 | 6 | 100.0% | 0 | 0 / 0 | n/a | 9 / 22 |
| seguimiento | 9 | 9 | 100.0% | 0 | 0 / 0 | n/a | 5 / 50 |
| transparencia | 11 | 11 | 100.0% | 0 | 0 / 0 | n/a | 2 / 7 |
| urgencias | 23 | 23 | 100.0% | 0 | 0 / 0 | n/a | 4 / 26 |

## By language

| Language | Tier | Cases | Passed | % | Filter blocks | Avg tokens in / out | Avg cost (USD) | Latency p50 / p95 (ms) |
|---|---|---|---|---|---|---|---|---|
| es | 1 | 62 | 62 | 100.0% | 2 | 0 / 0 | n/a | 5 / 42 |
| en | 1 | 61 | 61 | 100.0% | 2 | 0 / 0 | n/a | 4 / 10 |
| ht | 1 | 57 | 57 | 100.0% | 2 | 0 / 0 | n/a | 4 / 10 |
| fr | 2 | 10 | 10 | 100.0% | 1 | 0 / 0 | n/a | 2 / 7 |
| pt | 2 | 10 | 10 | 100.0% | 1 | 0 / 0 | n/a | 2 / 7 |

## Failures

None.
