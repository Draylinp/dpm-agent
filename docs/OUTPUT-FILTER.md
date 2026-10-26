# Output safety filter

*Versión en español: [es/FILTRO-SALIDA.md](es/FILTRO-SALIDA.md)*

Before any agent reply reaches the patient (web chat, WhatsApp, voice note or card photo), `IFiltroSalida` checks that it contains **no medical advice**: medications, doses or frequencies, treatments, interpretation of symptoms or results, diagnoses, or saying whether something is serious. Fixed system texts (reminders, offers, errors) are templates and are safe by construction.

## Layers

| Layer | What it is | Cost |
|---|---|---|
| 0 — rules (always on) | Regular expressions in Spanish, English, Haitian Creole, French and Portuguese: doses (`5 ml`, `500 mg`), frequencies (`cada 8 horas`, `every 8 hours`, `chak 8 è`), pill counts, and "take/give + medication" | Free, microseconds |
| 1 — classifier (configurable) | **Llama Guard** (Meta's open-weights safety model) or, if it is not deployed, the configured model with a strict classification prompt and JSON output | One extra short call per reply |

`FiltroSalida__Proveedor`:
- `LlamaGuard`: Llama Guard served by vLLM with an OpenAI-compatible endpoint (`deploy/modal/llama_guard_vllm.py`). It receives the turn (patient + agent) and answers `safe` or `unsafe` with the violated categories; category **S6 "Specialized Advice"** covers medical advice. Set `FiltroSalida__BaseUrl`, `__Model` and `__ApiKey`.
- `Modelo` (default): the main model (Muse Spark) as a classifier.
- `Reglas`: rules only (used with the scripted model in tests and CI).

The Meta developer documentation could not be reached from the environment where this was built, so the integration targets the open-weights Llama Guard through vLLM, which is the documented way to run it. If Meta exposes Llama Guard through its API, it only takes pointing `FiltroSalida__BaseUrl` to it, as long as it is OpenAI-compatible.

If the classifier fails (down, unreadable answer), `FiltroSalida__BloquearSiFalla=false` (default) lets the reply through — layer 0 already checked it — and logs a warning; `true` blocks it. Blocking every reply while the classifier is down would leave patients unattended.

## When it blocks

1. The reply is replaced with a safe message in the patient's language ("the medical staff must answer that; I've passed your message to the clinic; if it's an emergency, call {clinic's number}"). Tier-2 languages get the English text.
2. An escalation is created with the patient's original message and summaries in Spanish and English.
3. The audit log stores the blocked text, the reason and the layer that decided (`reglas`, `llama-guard`, `modelo`).
4. The blocked text is removed from the conversation history, so the model never sees it in the next turn.
5. The web chat trace shows a `filtro_salida` step.

## Measuring it

The evaluation bank counts blocks per category and language and flags blocks in categories where no clinical content is expected as **possible false positives**. Five cases (one per language) play a model that gives a dose on purpose: the filter must block all of them. See [EVALUATION.md](EVALUATION.md).
