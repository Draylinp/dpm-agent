# Privacy and patient data

Spanish version: [es/PRIVACIDAD.md](es/PRIVACIDAD.md).

This page lists the **technical measures** DPM Agent takes with patient data, following good practice and the Dominican personal data protection law (Ley 172-13). It is not legal advice and not a certification: each clinic is the data controller and should review it with its own advisor.

The patient-facing notice is `wwwroot/privacidad.html` (Spanish and English, with a Haitian Creole summary). It is linked from the web chat and from the consent text (`Cumplimiento__UrlPrivacidad`; in production, set an absolute `https://` address).

## What is stored, and for how long

| Data | Why | Retention |
|---|---|---|
| Name and phone number | Identify the patient (the phone comes from the channel, never from the model) | While a patient, or until a deletion request |
| Insurer and member number | Insurance paperwork; only after the patient confirms the card reading | Same |
| Appointments and waiting list | Scheduling | According to the clinic |
| Conversation messages | Context for the agent | 30 days (`Retencion__DiasConversaciones`) |
| WhatsApp channel messages | Delivery and retries | 30 days |
| Insurance card readings | Pending confirmation | 30 days after being resolved |
| Notice permissions (consent) | Proof of opt-in / opt-out, with channel and text version | While a patient |
| Audit log | Who did what and when | 1 year; the patient's text is cleared after 30 days |
| Model usage | Cost (tokens, latency) | Numbers only, with a pseudonymous session id |

**Never stored:** voice-note audio, card photos, national ID numbers. The agent is told not to ask for data it does not need (rule 15 of the prompt), and the evaluation bank checks it (`eval/casos/15-privacidad.json`).

## Patient rights from the dashboard

In **Patients and notices**, the receptionist searches the patient by phone and can:

- **Download a copy** (`GET /api/panel/pacientes/{id}/exportar`): JSON with the patient, appointments, waiting list, follow-ups, extra-slot requests, contact status (active, unreachable), the other patients managed from the same number (id and name only), consents, conversation messages (patient and clinic text only), escalations, card readings, insurance drafts and automatic notices. The export is recorded in the audit log.
- **Delete everything** (`POST /api/panel/pacientes/{id}/borrar` with `{"confirmacion":"BORRAR"}`): removes the patient, conversations and messages, consents, cards, escalations, appointments, waiting list, offers, insurance drafts and channel messages, in one transaction. The audit log keeps only the actions (who, what, when), with session, entity, detail and texts cleared, plus one `datos_borrados` entry with no personal data.

If the number also manages other patients (a family), deleting one of them removes only that patient's own data (appointments, follow-ups, extra-slot requests, consents, conversations linked to them); conversations and channel messages of the shared number stay, because they belong to the others too.

If the patient asks the agent, it escalates to the receptionist and shares the privacy notice (prompt rule 16).

**Contact status.** The receptionist can **deactivate** a patient (moved away, asked not to be contacted): no messages at all, follow-ups and waiting list cancelled, and their number no longer identifies anyone, since it may now belong to someone else. A number whose last 3 messages failed (configurable, `Contacto__FallosParaNoLocalizable`) or whose holder said "this is not that person" becomes **unreachable**: no more messages, notices go to "Calls to make" and the receptionist gets an escalation. Before contacting a follow-up ordered more than 3 months earlier, the first message only asks whether the number is still the patient's, with no doctor or health details.

## No patient data in logs, reports or errors

- WhatsApp session ids contain the phone number (`wa-8095550101`). Outside the database a stable pseudonym is used (`s-3fa2…`): in the log scope `SesionId`, in model usage records and in error logs (`Seguridad/Privacidad.cs`).
- Error bodies from Meta and WhatsApp, delivery errors and exceptions are logged masked: any run of 7+ digits becomes `[número]` and e-mail addresses `[correo]`. Unhandled exceptions are logged by `ManejadorErroresPrivado` after masking (the default ASP.NET log of the full exception is turned off).
- Tool arguments and results are never logged (they carry patient data).
- The evaluation report only quotes case ids and numbers; quoted replies in failures are masked.

These are tested, not just promised:

| Test | What it checks |
|---|---|
| `PrivacidadTests.Los_logs_de_un_flujo_completo_no_llevan_telefonos_ni_texto_de_pacientes` | Captures every log line (message, exception and scope values) of a web chat, a WhatsApp conversation where the model fails with a number in the exception, an escalation and a reminder, and searches for phone numbers, an ID number and the patient's text |
| `EvalTests.Todos_los_casos_pasan_con_el_modelo_falso` | The generated report has no phone numbers and no patient message |
| `DerechosDelPacienteTests` | Export content, deletion scope, anonymized audit log, other patients untouched |

## Security basics

HTTPS in production (HSTS), dashboard with secure cookies, hashed passwords and lockout, rate limiting, security headers, keys only in environment variables, and the WhatsApp webhook signature checked on every request. See the README.
