# Cost per conversation and per appointment

Spanish version: [es/COSTOS.md](es/COSTOS.md).

Every call to the model records **numbers only**: provider, model, purpose (`agente`, `filtro`, `juez`…), input and output tokens, latency, clinic and conversation id. Message content is never stored in this table (`UsosModelo`).

The record is written in the background (`ColaUso` → `GuardadoUsoHostedService`), so the patient's reply never waits for it. If the database is down, the oldest records are dropped instead of filling memory.

## Prices

Prices are **never hard-coded**: they change by provider and over time. Set them per model, per million tokens, in `appsettings.json`:

```json
"Precios": {
  "Moneda": "USD",
  "Modelos": {
    "muse-spark-1.2": { "EntradaPorMillon": 0.0, "SalidaPorMillon": 0.0 }
  }
}
```

Replace the zeros with the current prices from your provider. As environment variables, the model name contains `-` and `.`, which `export` does not accept; use `env` or your platform's settings screen (Azure App Service, Docker `environment:`):

```bash
env 'Precios__Modelos__muse-spark-1.2__EntradaPorMillon=<price>' 'Precios__Modelos__muse-spark-1.2__SalidaPorMillon=<price>' dotnet run --project src/DpmAgent.Api
```

A model without a price shows its cost as unknown (`—` / `n/a`) and is listed under "No price configured". It is never counted as free.

## Dashboard

`GET /api/panel/costos?dias=30` (staff only) and the **Costs** tab in the dashboard show, for the period:

| Field | Meaning |
|---|---|
| `conversaciones` | Conversations with at least one model call |
| `citasAgendadas` | Appointments booked by the agent (`cita_creada` and `oferta_aceptada` in the audit log, actor `agente`) |
| `llamadas`, `tokensEntrada`, `tokensSalida` | Totals |
| `costoTotal` | Sum over the models that have a price |
| `costoPorConversacion` | `costoTotal / conversaciones` |
| `costoPorCita` | `costoTotal / citasAgendadas` |
| `porModelo` | Calls, tokens and cost per model |

## Evaluation

The evaluation report (`tools/DpmAgent.Eval`) uses the same prices and adds the average cost per case to the tables by category and language. See [EVALUATION.md](EVALUATION.md).

## Not included yet

Voice transcription and the insurance-card reader call their own endpoints and are not counted in this table.
