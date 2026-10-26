# Conectar WhatsApp Cloud API

El agente recibe mensajes por el webhook `POST /api/whatsapp/webhook` y responde con WhatsApp Cloud API.

## 1. En Meta for Developers

1. Crea una app de tipo **Business** y agrega el producto **WhatsApp**.
2. En *WhatsApp → API Setup* anota el **Phone number ID** del número del consultorio.
3. Crea un **usuario del sistema** en Business Manager con permiso `whatsapp_business_messaging` y genera un **token permanente**.
4. En *App settings → Basic* copia el **App secret**.
5. En *WhatsApp → Configuration → Webhook*:
   - **Callback URL**: `https://TU-DOMINIO/api/whatsapp/webhook`
   - **Verify token**: un texto aleatorio que tú inventas (el mismo que pondrás en `WhatsApp__VerifyToken`).
   - Suscribe el campo **messages**.

## 2. Variables de entorno (nunca en el repositorio)

| Variable | Valor |
|---|---|
| `WhatsApp__AccessToken` | Token permanente del usuario del sistema |
| `WhatsApp__AppSecret` | App secret (valida la firma `X-Hub-Signature-256` de cada webhook) |
| `WhatsApp__VerifyToken` | El texto que pusiste como *Verify token* |
| `WhatsApp__PhoneNumberId` | Phone number ID del consultorio predeterminado |
| `WhatsApp__ApiVersion` | Versión de Graph API (por defecto `v21.0`) |

Con varios consultorios, guarda el `WhatsAppPhoneNumberId` de cada uno en la tabla `Consultorios`: el webhook enruta cada mensaje según el número que lo recibió.

## 3. Plantillas para mensajes que inicia el consultorio

WhatsApp solo deja enviar texto libre si el paciente escribió en las últimas 24 horas. Las ofertas de turnos y los recordatorios pueden caer fuera de esa ventana, así que en producción necesitan **plantillas aprobadas** (categoría *Utility*, idioma español). Crea estas en *WhatsApp Manager → Message templates* y pon sus nombres en la configuración:

| Clave | Variable | Cuerpo sugerido |
|---|---|---|
| `recordatorio` | `WhatsApp__Plantillas__recordatorio` | Hola, {{1}}. Le recordamos su cita con {{2}} el {{3}} a las {{4}} (cita {{5}}). Responda CONFIRMO para confirmar su asistencia o CANCELAR si no puede ir. |
| `oferta` | `WhatsApp__Plantillas__oferta` | Hola, {{1}}. Se liberó un turno con {{2}} el {{3}} a las {{4}}. ¿Lo quiere? Responda SÍ para reservarlo o NO para dejarlo pasar. La oferta vence a las {{5}}. |
| `oferta_vencida` | `WhatsApp__Plantillas__oferta_vencida` | El turno del {{1}} a las {{2}} ya no está disponible porque venció el tiempo para aceptarlo. Sigue en la lista de espera y le avisaremos si se libera otro. |

Si una clave no tiene plantilla, se envía como texto (sirve para pruebas dentro de la ventana de 24 horas).

## 4. Cómo funciona

1. Meta envía el mensaje al webhook. Se valida la firma con el App secret; si no coincide, se responde 401.
2. El mensaje se guarda en `MensajesEntrantes` (el id `wamid` es único, así que los reintentos de Meta no se procesan dos veces) y se responde 200 enseguida.
3. Un servicio en segundo plano lo procesa en orden:
   - **texto** (o botón de plantilla) → agente;
   - **nota de voz** → descarga → Muse Voice Transcribe → agente;
   - **foto** → descarga → lectura del carnet con Muse Spark → agente pide confirmación;
   - otros tipos → respuesta explicando qué se puede enviar.
4. La respuesta se encola en `MensajesSalientes` y el despachador la envía. Si el procesamiento falla 3 veces, se le pide disculpas al paciente y se crea un escalamiento.

La sesión de cada paciente en WhatsApp es `wa-` + su número de 10 dígitos, y queda atada a ese número.

## Probar en local

Expón la app con un túnel (por ejemplo `ngrok http 5271`) y usa esa URL como Callback URL. Con el número de prueba de Meta puedes escribirle desde tu WhatsApp personal si lo agregas como destinatario de prueba.
