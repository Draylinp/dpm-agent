# Conectar WhatsApp Cloud API

*English version: [../WHATSAPP.md](../WHATSAPP.md)*

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

WhatsApp solo deja enviar texto libre si el paciente escribió en las últimas 24 horas. Las ofertas de turnos y los recordatorios pueden caer fuera de esa ventana, así que en producción necesitan **plantillas aprobadas** (categoría *Utility*). Cada plantilla tiene un nombre y una traducción por idioma; el agente elige la traducción según el idioma del paciente (español, inglés o kreyòl) y usa la de inglés para cualquier otro idioma.

| Clave | Variable con el nombre |
|---|---|
| `recordatorio` | `WhatsApp__Plantillas__recordatorio` |
| `oferta` | `WhatsApp__Plantillas__oferta` |
| `oferta_vencida` | `WhatsApp__Plantillas__oferta_vencida` |
| `seguimiento_creado` | `WhatsApp__Plantillas__seguimiento_creado` |
| `seguimiento_agendado` | `WhatsApp__Plantillas__seguimiento_agendado` |
| `seguimiento_contacto` | `WhatsApp__Plantillas__seguimiento_contacto` |
| `recordatorio_7dias` | `WhatsApp__Plantillas__recordatorio_7dias` |

Códigos de idioma de cada traducción (`WhatsApp__IdiomasPlantillas__es`, `__en`, `__ht`; por defecto `es`, `en`, `ht`). Verifique en *WhatsApp Manager* qué códigos acepta su cuenta; si un idioma no está disponible, apúntelo al de inglés.

Listas para enviar a aprobación. Todas son plantillas de **Utilidad** (nunca de Marketing): tratan de una cita que el paciente ya tiene o pidió, nombran al consultorio (`{{1}}`) dentro del texto y terminan con cómo darse de baja (STOP). Ningún cuerpo empieza ni termina con una variable, algo que Meta rechaza. Los textos salen de `src/DpmAgent.Api/Idiomas/Recursos/` (los mismos que el sistema envía dentro de la ventana de 24 horas); las pruebas revisan en cada traducción el nombre del consultorio, la línea de baja y ese formato.

**recordatorio** — categoría *Utilidad*, variables: consultorio, primer nombre del paciente, médico, día, hora, id de la cita. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `martes 27 de octubre`, `9:00 a. m.`, `CITA-12`

- es: Hola, {{2}}. {{1}} le recuerda su cita con {{3}} el {{4}} a las {{5}} (cita {{6}}). Responda CONFIRMO para confirmar su asistencia o CANCELAR si no puede ir. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} reminds you of your appointment with {{3}} on {{4}} at {{5}} (appointment {{6}}). Reply CONFIRM to confirm or CANCEL if you can't make it. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} raple w randevou ou ak {{3}} jou {{4}} a {{5}} (randevou {{6}}). Reponn KONFIME pou konfime oswa ANILE si ou pa ka vini. Pou pa resevwa plis avi, reponn STOP.

**oferta** — categoría *Utilidad*, variables: consultorio, primer nombre del paciente, médico, día, hora, hora en que vence la oferta. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `José`, `Dra. Ana Valdez`, `martes 27 de octubre`, `9:00 a. m.`, `8:00 a. m.`

- es: Hola, {{2}}. {{1}} le avisa: se liberó un turno con {{3}} el {{4}} a las {{5}}. ¿Lo quiere? Responda SÍ para reservarlo o NO para dejarlo pasar. La oferta vence a las {{6}}. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. This is {{1}}: a slot opened up with {{3}} on {{4}} at {{5}}. Would you like it? Reply YES to book it or NO to let it go. This offer expires at {{6}}. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen: gen yon randevou ki libere ak {{3}} jou {{4}} a {{5}}. Èske ou vle l? Reponn WI pou pran l oswa NON pou kite l. Òf la ap fini a {{6}}. Pou pa resevwa plis avi, reponn STOP.

**oferta_vencida** — categoría *Utilidad*, variables: consultorio, día, hora. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `martes 27 de octubre`, `9:00 a. m.`

- es: Aviso de {{1}}: el turno del {{2}} a las {{3}} ya no está disponible porque venció el tiempo para aceptarlo. Sigue en la lista de espera y le avisaremos si se libera otro. Para no recibir más avisos, responda STOP.
- en: Notice from {{1}}: the slot on {{2}} at {{3}} is no longer available because the time to accept it ran out. You are still on the waiting list and we will let you know if another one opens up. To stop receiving notices, reply STOP.
- ht: Avi {{1}}: randevou {{2}} a {{3}} pa disponib ankò paske tan pou aksepte l la fini. Ou toujou sou lis datant lan e n ap fè ou konnen si gen yon lòt ki libere. Pou pa resevwa plis avi, reponn STOP.

**seguimiento_creado** — categoría *Utilidad*, variables: consultorio, primer nombre del paciente, médico, fecha aproximada. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 26 de abril`

- es: Hola, {{2}}. {{1}} le informa que {{3}} indicó un control para alrededor del {{4}}. Unas semanas antes le escribiremos para escoger el horario. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} would like you to know that {{3}} asked for a follow-up visit around {{4}}. A few weeks before, we will write to you so you can choose a time. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen {{3}} mande pou w retounen fè yon kontwòl anviwon {{4}}. Kèk semèn anvan, n ap ekri w pou w chwazi lè a. Pou pa resevwa plis avi, reponn STOP.

**seguimiento_agendado** — categoría *Utilidad*, variables: consultorio, primer nombre del paciente, médico, día, hora, id de la cita. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 9 de noviembre`, `8:00 a. m.`, `CITA-14`

- es: Hola, {{2}}. {{1}} le informa que {{3}} indicó un control y le reservamos cita el {{4}} a las {{5}} (cita {{6}}). Responda CONFIRMO para confirmarla o CAMBIAR si prefiere otro horario. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} would like you to know that {{3}} asked for a follow-up visit, and we booked it for {{4}} at {{5}} (appointment {{6}}). Reply CONFIRM to confirm it or CHANGE if you prefer another time. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} fè w konnen {{3}} mande yon kontwòl e nou rezève yon randevou pou ou jou {{4}} a {{5}} (randevou {{6}}). Reponn KONFIME pou konfime l oswa CHANJE si ou prefere yon lòt lè. Pou pa resevwa plis avi, reponn STOP.

**seguimiento_contacto** — categoría *Utilidad*, variables: consultorio, primer nombre del paciente, médico, fecha aproximada. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 26 de abril`

- es: Hola, {{2}}. {{1}} le recuerda que {{3}} indicó un control para alrededor del {{4}}. ¿Quiere que le busquemos un horario? Responda SÍ y le mostramos las opciones. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} reminds you that {{3}} asked for a follow-up visit around {{4}}. Would you like us to find a time for you? Reply YES and we will show you the options. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} raple w {{3}} mande yon kontwòl anviwon {{4}}. Èske ou vle nou chèche yon lè pou ou? Reponn WI e n ap montre w opsyon yo. Pou pa resevwa plis avi, reponn STOP.

**recordatorio_7dias** — categoría *Utilidad*, variables: consultorio, primer nombre del paciente, médico, día, hora, id de la cita. Valores de ejemplo para la revisión: `Consultorio Ejemplo`, `María`, `Dra. Ana Valdez`, `lunes 9 de noviembre`, `8:00 a. m.`, `CITA-14`

- es: Hola, {{2}}. {{1}} le recuerda que en una semana tiene su control con {{3}}, el {{4}} a las {{5}} (cita {{6}}). Si no puede ir, responda CAMBIAR. Para no recibir más avisos, responda STOP.
- en: Hi, {{2}}. {{1}} reminds you that in one week you have your follow-up visit with {{3}}, on {{4}} at {{5}} (appointment {{6}}). If you can't make it, reply CHANGE. To stop receiving notices, reply STOP.
- ht: Bonjou {{2}}. {{1}} raple w nan yon semèn ou gen kontwòl ou ak {{3}}, jou {{4}} a {{5}} (randevou {{6}}). Si ou pa ka vini, reponn CHANJE. Pou pa resevwa plis avi, reponn STOP.

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

## 5. Cumplimiento de las políticas de WhatsApp Business

Los términos de WhatsApp Business vigentes desde el 15 de enero de 2026 no permiten distribuir asistentes de IA de propósito general, pero sí que un negocio use IA para atender a sus propios clientes. Los mensajes que inicia el negocio requieren plantillas aprobadas y un consentimiento previo (opt-in) que nombre al negocio. Así lo cumple DPM Agent:

| Requisito | Cómo se cumple | Dónde |
|---|---|---|
| No es un asistente de propósito general | El prompt limita al agente a las tareas administrativas de este consultorio y le indica negarse con amabilidad, en el idioma del paciente, a todo lo demás. No hay ninguna herramienta general. El filtro de salida bloquea las respuestas con código y las reemplaza por el mensaje fijo de alcance. El banco de evaluación tiene la categoría `fuera_de_tema` (6 casos por idioma garantizado). | `Agent/ReceptionAgent.cs`, `Seguridad/FiltroSalida.cs`, `eval/casos/12-fuera_de_tema.json` |
| Transparencia | El servidor antepone a la primera respuesta de cada conversación "le atiende {nombre}, el asistente automático de {consultorio}" (nombre definido por el admin, `Clara` por defecto). "Hablar con una persona" / "talk to a person" / "pale ak yon moun" (también fr/pt) siempre crea un escalamiento, sin pasar por el modelo. | `Cumplimiento/PedidosDirectos.cs`, `eval/casos/13-transparencia.json` |
| Consentimiento que nombra al negocio | Registros `Consentimiento`: paciente, teléfono, categoría (`recordatorios`, `seguimientos`, `lista_espera`), estado, fecha, canal (`whatsapp`, `web`, `presencial`) y versión del texto mostrado. El texto nombra al consultorio, dice qué mensajes recibirá y cómo darse de baja, y enlaza el aviso de privacidad. Lo muestra el servidor (no lo escribe el modelo) y la respuesta solo se registra en un turno posterior, desde un mensaje real del paciente. La secretaria puede registrarlo en persona desde el panel. | `Cumplimiento/ServicioConsentimiento.cs`, `Cumplimiento/CumplimientoEndpoints.cs` |
| Ningún mensaje proactivo sin consentimiento | Antes de encolar un recordatorio o una oferta, el notificador revisa el consentimiento de esa categoría. Sin él no sale nada: el aviso aparece en el panel en **Llamadas pendientes**. | `Agenda/Notificador.cs` |
| Baja | Responder STOP (o BAJA, SISPANN, UNSUBSCRIBE…) revoca todas las categorías de una vez, sin el modelo. El agente también puede revocar algunas (`darse_de_baja`) y la secretaria puede registrar un retiro. Volver a activarlos requiere un consentimiento nuevo. | `Cumplimiento/PedidosDirectos.cs`, `Agent/AgentTools.cs` |
| Solo plantillas de utilidad | Ver la sección 3. Nunca se envía publicidad ni promociones. | `Idiomas/Recursos/` |

## Probar en local

Expón la app con un túnel (por ejemplo `ngrok http 5271`) y usa esa URL como Callback URL. Con el número de prueba de Meta puedes escribirle desde tu WhatsApp personal si lo agregas como destinatario de prueba.
