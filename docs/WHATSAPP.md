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

Language code for each translation: `WhatsApp__IdiomasPlantillas__es`, `__en`, `__ht` (defaults `es`, `en`, `ht`). Check in *WhatsApp Manager* which codes your account accepts; if a language is not available, point it to the English one.

Suggested bodies (the same texts the system uses, in `src/DpmAgent.Api/Idiomas/Recursos/`):

**recordatorio**
- es: Hola, {{1}}. Le recordamos su cita con {{2}} el {{3}} a las {{4}} (cita {{5}}). Responda CONFIRMO para confirmar su asistencia o CANCELAR si no puede ir.
- en: Hi, {{1}}. This is a reminder of your appointment with {{2}} on {{3}} at {{4}} (appointment {{5}}). Reply CONFIRM to confirm or CANCEL if you can't make it.
- ht: Bonjou {{1}}. Nou raple ou randevou ou ak {{2}} {{3}} a {{4}} (randevou {{5}}). Reponn KONFIME pou konfime oswa ANILE si ou pa ka vini.

**oferta**
- es: Hola, {{1}}. Se liberó un turno con {{2}} el {{3}} a las {{4}}. ¿Lo quiere? Responda SÍ para reservarlo o NO para dejarlo pasar. La oferta vence a las {{5}}.
- en: Hi, {{1}}. A slot opened up with {{2}} on {{3}} at {{4}}. Would you like it? Reply YES to book it or NO to let it go. This offer expires at {{5}}.
- ht: Bonjou {{1}}. Gen yon randevou ki libere ak {{2}} {{3}} a {{4}}. Èske ou vle l? Reponn WI pou pran l oswa NON pou kite l. Òf la ap fini a {{5}}.

**oferta_vencida**
- es: El turno del {{1}} a las {{2}} ya no está disponible porque venció el tiempo para aceptarlo. Sigue en la lista de espera y le avisaremos si se libera otro.
- en: The slot on {{1}} at {{2}} is no longer available because the time to accept it ran out. You are still on the waiting list and we will let you know if another one opens up.
- ht: Randevou {{1}} a {{2}} pa disponib ankò paske tan pou aksepte l la fini. Ou toujou sou lis datant lan e n ap fè ou konnen si gen yon lòt ki libere.

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

## Local testing

Expose the app with a tunnel (for example `ngrok http 5271`) and use that URL as the Callback URL. With Meta's test number you can message it from your personal WhatsApp once you add it as a test recipient.
