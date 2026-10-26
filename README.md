# DPM Agent

*Versión en español: [README.es.md](README.es.md)*

A front-desk agent for medical clinics in the Dominican Republic, built on **Meta's Muse models**.

The agent talks to patients over chat and WhatsApp: it finds or registers the patient, checks the doctor's schedule, offers slots, books, reschedules and cancels appointments, and hands everything outside its job to the human staff. **It is strictly administrative: it never gives medical advice.**

> Project for the **Meta AI Developer Hackathon** (Oct 26 – Nov 4, 2026), Indie Developer track.

---

## The problem

In many Dominican clinics the schedule runs on WhatsApp and the phone. The receptionist answers the same messages all day ("any slot tomorrow?", "does the doctor work on Saturday?", "I want to change my appointment") while also attending patients in the waiting room, filling in insurance (ARS) forms and taking payments. Messages go unanswered, patients are lost and double bookings happen.

DPM Agent takes over that repetitive conversation and leaves the staff what truly needs a person.

## What it does today

- **Multilingual**: Spanish, English and Haitian Creole (Kreyòl ayisyen) are guaranteed (fixed texts, WhatsApp templates and tests in all three); any other language is answered as well as possible, with English as the fallback for fixed texts. The language is detected on every message and the conversation follows the patient if they switch. The web chat and the dashboard have a Spanish/English selector.
- Short, friendly replies, like a WhatsApp message.
- **Multi-step tool calling**: the model decides which tools to use and in which order (find patient → check availability → confirm → book).
- Server-side validation: a taken or past slot cannot be booked, even if the model tries.
- **Voice notes**: `POST /api/chat/audio` receives the audio (WhatsApp OGG/Opus, WebM, MP3, M4A or WAV), transcribes it with Muse Voice Transcribe and passes it to the agent. The format is checked from the bytes, there are size and duration limits, and the audio is never stored. The web chat has a button to record from the browser.
- **Insurance card from a photo**: `POST /api/pacientes/carnet` reads name, insurer (ARS) and member number with Muse Spark (image input + structured output with a JSON schema). The result stays **pending** until the patient confirms it in the chat (`confirmar_datos_carnet`) or the receptionist reviews it. If the number had no patient, one is registered with that data. The image is not stored; what was read is cleaned as untrusted data and, if the name does not match the patient of that number, only the receptionist can confirm it.
- **A waiting list that fills gaps on its own**: if no slot works, the agent adds the patient to the waiting list (`agregar_lista_espera`). When someone cancels, a background service offers the slot to the first patient whose preferred days and hours match, on their channel. If they don't answer in 30 minutes the offer expires and moves to the next one; if they accept (`responder_oferta`) the slot is booked after checking again that it is still free.
- **Reminder 24 hours before**, with the option to confirm (`confirmar_asistencia`) or cancel.
- **Audit log** (`GET /api/bitacora`): who did what and when (agent, system or receptionist). Automatic messages are fixed templates, not model output.
- **WhatsApp Cloud API**: webhook with `X-Hub-Signature-256` verification, text, voice notes and card photos, replies and proactive messages with approved templates. See [docs/WHATSAPP.md](docs/WHATSAPP.md).
- **Clinic dashboard** (`/panel.html`): sign-in with a secure cookie (`HttpOnly`, `SameSite=Strict`), passwords hashed with `PasswordHasher` and lockout after 5 attempts. Shows today's appointments, escalations (urgent first, with the patient's original message and a summary in the dashboard language), insurance cards to confirm and the audit log. Each receptionist only sees their own clinic's data.
- **Insurance authorization draft**: from today's appointments, the receptionist requests a draft with the administrative data (insurer, member, doctor, specialty, date). Missing data is marked `[FALTA: …]` and blocks approval. Approving only marks it as ready: **it is never sent on its own**.
- **Persistence and multi-clinic**: appointments, patients, escalations and conversations are stored with EF Core (SQLite locally, PostgreSQL in the cloud). Every record carries its `ConsultorioId` and every query is filtered by the current clinic.
- **Escalation to staff**: clinical questions, complaints or possible emergencies go to a human. In a possible emergency the agent tells the patient to call the clinic's emergency number (911 by default).
- The web chat shows, next to the conversation, every tool the model used and what it returned.

## How it uses Meta AI

| Piece | Model / product |
|---|---|
| Reasoning and tool calling | **Muse Spark** through the Meta Model API (`/v1/chat/completions`) |
| Reading the insurance card | Muse Spark, image input + structured output (`/api/pacientes/carnet`) |
| Voice-note transcription | Muse Voice Transcribe (`/api/chat/audio`) |
| Own server or fallback | **Muse Glimmer**, open weights, served with vLLM (Modal, Baseten or on-premises) |

The model sits behind `IChatModel`: `Modelo__Proveedor` picks the Meta Model API or an OpenAI-compatible server, and `Modelo__Respaldo` sets the fallback if the main one fails. See [docs/GLIMMER.md](docs/GLIMMER.md).

## Evaluation

89 cases in 11 categories (booking, rescheduling, cancelling, new patient, insurance card, waiting list, clinical question, emergency, prompt injection, someone else's appointment, language switch) in Spanish, English and Haitian Creole, plus French and Portuguese for tier 2. Two layers: deterministic checks on tools and database state, and an LLM judge (Muse Glimmer by default) with a written rubric. See [docs/EVALUATION.md](docs/EVALUATION.md).

**Against Muse Spark:**

<!-- RESULTADOS MUSE SPARK: run `dotnet run --project tools/DpmAgent.Eval -- --modelo real` and paste the tables from eval/resultados/ here -->
*Pending: requires access to the Meta Model API.*

**Server guarantees (scripted model, runs in CI on every push):** 89 / 89 cases pass — [latest report](eval/resultados/2026-10-26-modelo-falso.md). Some scripts play a model that obeys an injection on purpose; the server still blocks cancelling someone else's appointment or cancelling without the patient's confirmation.

## Architecture

```
Browser / WhatsApp
        │
        ▼
 ASP.NET Core 8 (Minimal API)
   POST /api/chat        POST /api/chat/audio ──► ITranscriber ──► Muse Voice Transcribe
        │                        │ (transcribed text)
        ├────────────────────────┘
   ReceptionAgent ──► IChatModel ──► Meta Model API (Muse Spark)
        │                             or own server (Muse Glimmer)
        ▼
   AgentTools (12 administrative tools)
        │
        ▼
   IDpmGateway ──► DbDpmGateway (EF Core: SQLite / PostgreSQL)
        │          MockDpmGateway (in memory, for tests)
   IConversationStore ──► DbConversationStore / InMemoryConversationStore
```

```
src/DpmAgent.Api/
  Agent/          ReceptionAgent, AgentTools, Sesion, IConversationStore
  Idiomas/        language detection, fixed texts in es/en/ht
  Data/           DpmDbContext, entities, migrations per provider, demo data
  Model/          IChatModel, Meta and OpenAI-compatible clients, fallback
  Agenda/         waiting list, offer and reminder engine, notifier, dispatcher, audit log
  Autorizaciones/ insurance authorization drafts
  Canales/        IMessageChannel, WebChatChannel, WhatsApp/ (Cloud API, webhook, processor)
  Carnet/         card reader, confirmation service, endpoints
  Panel/          users, cookie authentication, receptionist views
  Voz/            transcriber, audio validation, voice-note endpoint
  Seguridad/      rate limiting, health, logs, retention
  Dpm/            IDpmGateway, DbDpmGateway, MockDpmGateway, clinic hours
  wwwroot/        web chat and dashboard
tests/DpmAgent.Tests/   tests with a scripted fake model (no real calls)
```

(Code identifiers are in Spanish; the code is the project's working language.)

## Run locally

Requirements: .NET 8 SDK and a Meta Model API key.

```bash
git clone https://github.com/Draylinp/dpm-agent.git
cd dpm-agent

# Linux / macOS
export META_API_KEY="your-key"
# Windows PowerShell
# $env:META_API_KEY="your-key"

dotnet run --project src/DpmAgent.Api
```

Open the URL printed in the console (for example `http://localhost:5271`). The *WhatsApp number (simulated)* field acts as the sender: with `809-555-0101` the agent recognizes you as the fictional patient María Rodríguez. Try:
*"Hi, I would like an appointment with the doctor tomorrow morning."*

On startup the app applies the migrations and creates `dpm-agent.db` (SQLite) with a clinic, two doctors and two fictional patients. Appointments and conversations survive restarts.

Tests:

```bash
dotnet test
```

With a Meta key, `./scripts/probar-meta.sh` and `dotnet test --filter "Categoria=Meta"` confirm the real formats. See [docs/META-API-TESTING.md](docs/META-API-TESTING.md).

The PostgreSQL test only runs when `DPM_TEST_POSTGRES` holds the connection string of a throwaway database (CI provides one with a Postgres container).

Deployment to Azure App Service with PostgreSQL, Key Vault and GitHub Actions: see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). There is also a multi-stage `Dockerfile`.

Main settings (`appsettings.json` or environment variables):

| Variable | Default |
|---|---|
| `Meta__Model` | `muse-spark-1.2` (`-contributor` variants are rejected on startup) |
| `Modelo__Proveedor` | `MetaApi` or `OpenAICompatible` |
| `Modelo__Respaldo` | empty, or the other provider |
| `Meta__BaseUrl` | `https://api.meta.ai/v1/` |
| `Transcripcion__Modelo` | `muse-voice-transcribe-1.0` |
| `Transcripcion__Idioma` | empty (the service detects the language) |
| `Carnet__Modelo` | empty (uses `Meta__Model`) |
| `BaseDatos__Proveedor` | `Sqlite` (also `Postgres`, or `Memoria` = in-memory SQLite) |
| `Panel__ClaveInicial` | Password of the initial `secretaria` (admin) user. In development, if missing, one is generated and printed to the console |
| `Agenda__OfertaMinutos` | `30` |
| `Agenda__RecordatorioHorasAntes` | `24` |
| `ConnectionStrings__Dpm` | `Data Source=dpm-agent.db` (in the cloud, the PostgreSQL connection string as an environment variable) |

The clinic's default language (es/en) and emergency number are set from the dashboard (`PUT /api/panel/consultorio`, admin only).

## Security and limits

- **No medical advice.** The system prompt forbids it in every language and there is no clinical tool. Any health question is escalated to the staff.
- **Output safety filter**: every model reply is checked before it is sent — multilingual rules for doses and medications, then Llama Guard (open weights) or the configured model as a classifier. A blocked reply is replaced with a safe message, escalated and recorded in the audit log. See [docs/OUTPUT-FILTER.md](docs/OUTPUT-FILTER.md).
- **Each conversation only acts on its own patient.** The patient is identified by the phone number the channel provides (the WhatsApp sender), never by what the model says. Cancelling someone else's appointment returns the same error as a non-existent one. A session cannot change its number.
- **Prompt-injection defense enforced on the server**: tool results, transcriptions and text read from photos are treated as data. Cancelling is a two-step process controlled by the server: it only executes on a later turn, i.e. after a real patient message. A turn cannot perform more than 3 data-changing actions nor more than 5 tools per response. The appointment reason only accepts administrative values.
- **Rate limiting** per IP (public endpoints and sign-in) and per conversation (all channels).
- **Structured logs** (JSON in production) with `SesionId` and `CorrelationId`, without tool arguments or results: no patient data.
- **Retention**: conversations are deleted after 30 days (configurable); the patient's original text copied into the audit log is cleared at the same time; the log itself is kept for a year.
- **Health**: `/health/live` and `/health/ready` (database and model configured).
- Security headers (CSP, `X-Frame-Options`, `nosniff`), HSTS and HTTPS in production.
- **Fictional data only.** Every patient and doctor in the repository is made up.
- Keys are read from environment variables and never stored in the repository.

## What existed before the event and what was built during it

**Before October 26, 2026:** DPM, my clinic management system (a separate private repository). That system is **not** part of this submission or the demo.

**During the hackathon (everything in this repository):** the agent, the Meta Model API clients, the tools, the DPM simulation, the web chat, the dashboard and the tests. The commit history starts on October 26, 2026.

## License

MIT
