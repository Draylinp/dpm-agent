# Connecting WhatsApp Cloud API

*Versión en español: [es/WHATSAPP.md](es/WHATSAPP.md)*

The agent receives messages on the webhook `POST /api/whatsapp/webhook` and answers through WhatsApp Cloud API.

## 1. In Meta for Developers

1. Create a **Business** app and add the **WhatsApp** product.
2. In *WhatsApp → API Setup*, write down the **Phone number ID** of the clinic's number.
3. Create a **system user** in Business Manager with the `whatsapp_business_messaging` permission and generate a **permanent token**.
4. In *App settings → Basic*, copy the **App secret**.
5. In *WhatsApp → Configuration → Webhook*:
   - **Callback URL**: `https://YOUR-DOMAIN/api/whatsapp/webhook`
   - **Verify token**: a random string you make up (the same one you put in `WhatsApp__VerifyToken`).
   - Subscribe to the **messages** field.

## 2. Environment variables (never in the repository)

| Variable | Value |
|---|---|
| `WhatsApp__AccessToken` | Permanent system-user token |
| `WhatsApp__AppSecret` | App secret (validates the `X-Hub-Signature-256` signature of every webhook) |
| `WhatsApp__VerifyToken` | The string you used as *Verify token* |
| `WhatsApp__PhoneNumberId` | Phone number ID of the default clinic |
| `WhatsApp__ApiVersion` | Graph API version (default `v21.0`) |

With several clinics, store each one's `WhatsAppPhoneNumberId` in the `Consultorios` table: the webhook routes each message by the number that received it.

## 3. Templates for clinic-initiated messages

WhatsApp only allows free-form text if the patient wrote in the last 24 hours. Slot offers and reminders can fall outside that window, so in production they need **approved templates** (category *Utility*). Each template has one name and one translation per language; the agent picks the translation by the patient's language (Spanish, English or Haitian Creole) and uses the English one for any other language.

| Key | Variable holding the template name |
|---|---|
| `recordatorio` (reminder) | `WhatsApp__Plantillas__recordatorio` |
| `oferta` (slot offer) | `WhatsApp__Plantillas__oferta` |
| `oferta_vencida` (offer expired) | `WhatsApp__Plantillas__oferta_vencida` |
| `seguimiento_creado` | `WhatsApp__Plantillas__seguimiento_creado` |
| `seguimiento_agendado` | `WhatsApp__Plantillas__seguimiento_agendado` |
| `seguimiento_contacto` | `WhatsApp__Plantillas__seguimiento_contacto` |
| `recordatorio_7dias` | `WhatsApp__Plantillas__recordatorio_7dias` |
| `resumen_medico` | `WhatsApp__Plantillas__resumen_medico` |
| `sobrecupo_medico` | `WhatsApp__Plantillas__sobrecupo_medico` |
| `sobrecupo_aprobado` | `WhatsApp__Plantillas__sobrecupo_aprobado` |
| `sobrecupo_rechazado` | `WhatsApp__Plantillas__sobrecupo_rechazado` |

Language code for each translation: `WhatsApp__IdiomasPlantillas__es`, `__en`, `__ht` (defaults `es`, `en`, `ht`). Check in *WhatsApp Manager* which codes your account accepts; if a language is not available, point it to the English one.

Ready to submit for approval. All of them are **Utility** templates (never Marketing): they are about an appointment the patient already has or asked for, name the clinic (`{{1}}`) inside the text, and end with how to opt out (STOP). No body starts or ends with a variable, which Meta rejects. The texts come from `src/DpmAgent.Api/Idiomas/Recursos/` (the same ones the system sends inside the 24-hour window); tests check every translation for the clinic name, the opt-out line and that format.

**recordatorio** — category *Utility*, variables: clinic, patient first name, doctor, day, time, appointment id. Sample values for review: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `martes 27 de octubre`, `9:00 a. m.`, `CITA-12`

- es: Hola, {{2}}. {{1}} le recuerda su cita con {{3}} el {{4}} a las {{5}} (cita {{6}}). Responda CONFIRMO para confirmar su asistencia o CANCELAR si no puede ir. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} reminds you of your appointment with {{3}} on {{4}} at {{5}} (appointment {{6}}). Reply CONFIRM to confirm or CANCEL if you can't make it. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} raple w randevou ou ak {{3}} jou {{4}} a {{5}} (randevou {{6}}). Reponn KONFIME pou konfime oswa ANILE si ou pa ka vini. Pou pa resevwa plis avi, reponn STOP.

**oferta** — category *Utility*, variables: clinic, patient first name, doctor, day, time, offer expiry time. Sample values for review: `Consultorio Ejemplo`, `José`, `Dra. Ana Valdez`, `martes 27 de octubre`, `9:00 a. m.`, `8:00 a. m.`

- es: Hola, {{2}}. {{1}} le avisa: se liberó un turno con {{3}} el {{4}} a las {{5}}. ¿Lo quiere? Responda SÍ para reservarlo o NO para dejarlo pasar. La oferta vence a las {{6}}. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. This is {{1}}: a slot opened up with {{3}} on {{4}} at {{5}}. Would you like it? Reply YES to book it or NO to let it go. This offer expires at {{6}}. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen: gen yon randevou ki libere ak {{3}} jou {{4}} a {{5}}. Èske ou vle l? Reponn WI pou pran l oswa NON pou kite l. Òf la ap fini a {{6}}. Pou pa resevwa plis avi, reponn STOP.

**oferta_vencida** — category *Utility*, variables: clinic, day, time. Sample values for review: `Consultorio Ejemplo`, `martes 27 de octubre`, `9:00 a. m.`

- es: Aviso de {{1}}: el turno del {{2}} a las {{3}} ya no está disponible porque venció el tiempo para aceptarlo. Sigue en la lista de espera y le avisaremos si se libera otro. Para no recibir más avisos, responda STOP.
- en: Notice from {{1}}: the slot on {{2}} at {{3}} is no longer available because the time to accept it ran out. You are still on the waiting list and we will let you know if another one opens up. To stop receiving notices, reply STOP.
- ht: Avi {{1}}: randevou {{2}} a {{3}} pa disponib ankò paske tan pou aksepte l la fini. Ou toujou sou lis datant lan e n ap fè ou konnen si gen yon lòt ki libere. Pou pa resevwa plis avi, reponn STOP.

**seguimiento_creado** — category *Utility*, variables: clinic, patient first name, doctor, approximate date. Sample values for review: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 26 de abril`

- es: Hola, {{2}}. {{1}} le informa que {{3}} indicó un control para alrededor del {{4}}. Unas semanas antes le escribiremos para escoger el horario. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} would like you to know that {{3}} asked for a follow-up visit around {{4}}. A few weeks before, we will write to you so you can choose a time. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen {{3}} mande pou w retounen fè yon kontwòl anviwon {{4}}. Kèk semèn anvan, n ap ekri w pou w chwazi lè a. Pou pa resevwa plis avi, reponn STOP.

**seguimiento_agendado** — category *Utility*, variables: clinic, patient first name, doctor, day, time, appointment id. Sample values for review: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 9 de noviembre`, `8:00 a. m.`, `CITA-14`

- es: Hola, {{2}}. {{1}} le informa que {{3}} indicó un control y le reservamos cita el {{4}} a las {{5}} (cita {{6}}). Responda CONFIRMO para confirmarla o CAMBIAR si prefiere otro horario. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} would like you to know that {{3}} asked for a follow-up visit, and we booked it for {{4}} at {{5}} (appointment {{6}}). Reply CONFIRM to confirm it or CHANGE if you prefer another time. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen {{3}} mande yon kontwòl e nou rezève yon randevou pou ou jou {{4}} a {{5}} (randevou {{6}}). Reponn KONFIME pou konfime l oswa CHANJE si ou prefere yon lòt lè. Pou pa resevwa plis avi, reponn STOP.

**seguimiento_contacto** — category *Utility*, variables: clinic, patient first name, doctor, approximate date. Sample values for review: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 26 de abril`

- es: Hola, {{2}}. {{1}} le recuerda que {{3}} indicó un control para alrededor del {{4}}. ¿Quiere que le busquemos un horario? Responda SÍ y le mostramos las opciones. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} reminds you that {{3}} asked for a follow-up visit around {{4}}. Would you like us to find a time for you? Reply YES and we will show you the options. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} raple w {{3}} mande yon kontwòl anviwon {{4}}. Èske ou vle nou chèche yon lè pou ou? Reponn WI e n ap montre w opsyon yo. Pou pa resevwa plis avi, reponn STOP.

**recordatorio_7dias** — category *Utility*, variables: clinic, patient first name, doctor, day, time, appointment id. Sample values for review: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 9 de noviembre`, `8:00 a. m.`, `CITA-14`

- es: Hola, {{2}}. {{1}} le recuerda que en una semana tiene su control con {{3}}, el {{4}} a las {{5}} (cita {{6}}). Si no puede ir, responda CAMBIAR. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} reminds you that in one week you have your follow-up visit with {{3}}, on {{4}} at {{5}} (appointment {{6}}). If you can't make it, reply CHANGE. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} raple w nan yon semèn ou gen kontwòl ou ak {{3}}, jou {{4}} a {{5}} (randevou {{6}}). Si ou pa ka vini, reponn CHANJE. Pou pa resevwa plis avi, reponn STOP.

**resumen_medico** — category *Utility*, sent to the **doctor** (only with their own permission, category `resumen_medico`), variables: clinic, doctor, appointments, extra-slot requests, follow-ups with no answer. Sample values for review: `Consultorio Ejemplo`, `Dr. Luis Herrera`, `6`, `1`, `2`

- es: Buenos días, {{2}}. Resumen de {{1}} para hoy: {{3}} citas, {{4}} sobrecupos por decidir y {{5}} pacientes de seguimiento sin respuesta. Responda AGENDA para ver el detalle. Para no recibir este resumen, responda STOP.
- en: Good morning, {{2}}. Summary from {{1}} for today: {{3}} appointments, {{4}} extra-slot requests to decide and {{5}} follow-up patients with no answer. Reply AGENDA to see the details. To stop receiving this summary, reply STOP.
- ht: Bonjou {{2}}. Rezime {{1}} pou jodi a: {{3}} randevou, {{4}} demann plas siplemantè pou deside ak {{5}} pasyan swivi ki pa reponn. Reponn AGENDA pou wè detay yo. Pou pa resevwa rezime sa a ankò, reponn STOP.

**sobrecupo_medico** — category *Utility*, sent to the **doctor** (verified number, only with their permission, category `resumen_medico`) the moment a patient asks for an extra slot. Variables: clinic, doctor, patient, day, request id. Sample values for review: `Consultorio Ejemplo`, `Dr. Luis Herrera`, `María Rodríguez`, `lunes 26 de octubre`, `SOB-001`

- es: Hola, {{2}}. {{1}} le avisa que {{3}} pidió que lo vean lo antes posible el {{4}} y no hay espacio en la agenda. Responda APROBAR {{5}} con la hora, o RECHAZAR {{5}}. Para no recibir estos avisos, responda STOP.
- en: Hi, {{2}}. {{1}} lets you know that {{3}} asked to be seen as soon as possible on {{4}} and the schedule is full. Reply APROBAR {{5}} with the time, or RECHAZAR {{5}}. To stop receiving these notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen {{3}} mande pou yo wè l pi vit posib jou {{4}} e pa gen plas nan orè a. Reponn APROBAR {{5}} ak lè a, oswa RECHAZAR {{5}}. Pou pa resevwa avi sa yo, reponn STOP.

**sobrecupo_aprobado** — category *Utility*, the answer to the patient's own request for an extra slot. Variables: clinic, patient first name, doctor, day, time, appointment id, emergency number. Sample values for review: `Consultorio Ejemplo`, `María`, `Dr. Luis Herrera`, `lunes 26 de octubre`, `5:30 p. m.`, `CITA-15`, `911`

- es: Hola, {{2}}. {{1}} le confirma que {{3}} le atenderá el {{4}} a las {{5}} (cita {{6}}). Si es una emergencia antes de esa hora, llame al {{7}}. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} confirms that {{3}} will see you on {{4}} at {{5}} (appointment {{6}}). If there is an emergency before then, call {{7}}. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} konfime {{3}} ap wè w jou {{4}} a {{5}} (randevou {{6}}). Si gen yon ijans anvan lè sa a, rele {{7}}. Pou pa resevwa plis avi, reponn STOP.

**sobrecupo_rechazado** — category *Utility*, the answer to the patient's own request when staff could not open an extra slot. Variables: clinic, patient first name, day, emergency number. Sample values for review: `Consultorio Ejemplo`, `María`, `lunes 26 de octubre`, `911`

- es: Hola, {{2}}. {{1}} le informa que no fue posible abrir un espacio fuera de agenda el {{3}}. Responda CITA y le buscamos el próximo horario disponible. Si es una emergencia, llame al {{4}}. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} lets you know that it was not possible to open an extra slot on {{3}}. Reply APPOINTMENT and we will find the next available time. If this is an emergency, call {{4}}. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen li pa t posib pou louvri yon plas siplemantè jou {{3}}. Reponn RANDEVOU e n ap chèche pwochen lè ki disponib. Si se yon ijans, rele {{4}}. Pou pa resevwa plis avi, reponn STOP.

If a key has no template, the message is sent as text (useful for testing inside the 24-hour window).

## 4. How it works

1. Meta posts the message to the webhook. The signature is validated with the App secret; if it does not match, the answer is 401.
2. The message is stored in `MensajesEntrantes` (the `wamid` id is unique, so Meta's retries are never processed twice) and the webhook returns 200 right away.
3. A background service processes messages in order:
   - **text** (or a template button) → agent;
   - **voice note** → download → Muse Voice Transcribe → agent;
   - **photo** → download → insurance-card reading with Muse Spark → the agent asks the patient to confirm;
   - other types → a reply explaining what can be sent.
4. The language of each message is detected and kept in the session; fixed texts and errors go out in the patient's language.
5. The reply is queued in `MensajesSalientes` and the dispatcher sends it. If processing fails 3 times, the patient gets an apology and an escalation is created.

Each patient's WhatsApp session is `wa-` + their 10-digit number and is bound to that number.

## 5. WhatsApp Business policy compliance

The WhatsApp Business terms in effect since January 15, 2026 do not allow distributing general-purpose AI assistants, but they do allow a business to use AI to serve its own customers. Business-initiated messages need approved templates and prior opt-in that names the business. This is how DPM Agent meets each point:

| Requirement | How it is met | Where |
|---|---|---|
| Not a general-purpose assistant | The prompt limits the agent to this clinic's administrative tasks and tells it to refuse anything else politely in the patient's language. There is no general tool. Replies with code are blocked by the output filter and replaced with the fixed out-of-scope message. The evaluation bank has a `fuera_de_tema` category (6 cases per guaranteed language). | `Agent/ReceptionAgent.cs`, `Seguridad/FiltroSalida.cs`, `eval/casos/12-fuera_de_tema.json` |
| Transparency | The first reply of every conversation is prefixed by the server with "this is {name}, the automated assistant of {clinic}" (name set by the admin, `Clara` by default). "Talk to a person" / "hablar con una persona" / "pale ak yon moun" (also fr/pt) always creates an escalation, without going through the model. | `Cumplimiento/PedidosDirectos.cs`, `eval/casos/13-transparencia.json` |
| Opt-in that names the business | `Consentimiento` records: patient, phone, category (`recordatorios`, `seguimientos`, `lista_espera`), state, date, channel (`whatsapp`, `web`, `presencial`) and the version of the text shown. The text names the clinic, says which messages the patient will get and how to opt out, and links the privacy notice. It is shown by the server (not written by the model), and the answer is only recorded on a later turn, from a real patient message. The receptionist can record it in person from the dashboard. | `Cumplimiento/ServicioConsentimiento.cs`, `Cumplimiento/CumplimientoEndpoints.cs` |
| No proactive message without opt-in | Before queuing a reminder or an offer, the notifier checks the consent for that category. Without it nothing is sent: the notice appears in the dashboard under **Calls to make**. | `Agenda/Notificador.cs` |
| Opt-out | Replying STOP (or BAJA, SISPANN, UNSUBSCRIBE…) revokes every category at once, without the model. The agent can also revoke some categories (`darse_de_baja`), and the receptionist can record a withdrawal. Opting back in needs a new consent. | `Cumplimiento/PedidosDirectos.cs`, `Agent/AgentTools.cs` |
| Utility templates only | See section 3. No marketing or promotions are ever sent. | `Idiomas/Recursos/` |

## Local testing

Expose the app with a tunnel (for example `ngrok http 5271`) and use that URL as the Callback URL. With Meta's test number you can message it from your personal WhatsApp once you add it as a test recipient.
