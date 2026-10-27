# Privacidad y datos de pacientes

Versión en inglés: [../PRIVACY.md](../PRIVACY.md).

Esta página enumera las **medidas técnicas** que DPM Agent aplica a los datos de pacientes, siguiendo buenas prácticas y la ley dominicana de protección de datos personales (Ley 172-13). No es asesoría legal ni una certificación: cada consultorio es responsable de sus datos y debe revisarla con su asesor.

El aviso para pacientes es `wwwroot/privacidad.html` (español e inglés, con resumen en kreyòl). Está enlazado desde el chat web y desde el texto de consentimiento (`Cumplimiento__UrlPrivacidad`; en producción, ponga una dirección absoluta `https://`).

## Qué se guarda y por cuánto tiempo

| Dato | Para qué | Conservación |
|---|---|---|
| Nombre y teléfono | Identificar al paciente (el teléfono lo da el canal, nunca el modelo) | Mientras sea paciente, o hasta que pida el borrado |
| ARS y número de afiliado | Trámites de la ARS; solo después de que el paciente confirma la lectura del carnet | Igual |
| Citas y lista de espera | Agenda | Según el consultorio |
| Mensajes de la conversación | Contexto del agente | 30 días (`Retencion__DiasConversaciones`) |
| Mensajes del canal de WhatsApp | Entrega y reintentos | 30 días |
| Lecturas de carnet | Pendientes de confirmar | 30 días después de resueltas |
| Permisos de avisos (consentimiento) | Prueba del opt-in y de la baja, con canal y versión del texto | Mientras sea paciente |
| Bitácora | Quién hizo qué y cuándo | 1 año; el texto del paciente se borra a los 30 días |
| Uso del modelo | Costo (tokens, latencia) | Solo cifras, con un seudónimo de la sesión |

**Nunca se guarda:** el audio de las notas de voz, las fotos del carnet ni la cédula. El agente tiene instrucción de no pedir datos que no necesita (regla 15 del prompt) y el banco de evaluación lo comprueba (`eval/casos/15-privacidad.json`).

## Derechos del paciente desde el panel

En **Pacientes y avisos**, la secretaria busca al paciente por teléfono y puede:

- **Descargar una copia** (`GET /api/panel/pacientes/{id}/exportar`): JSON con el paciente, citas, lista de espera, permisos, mensajes de la conversación (solo el texto del paciente y del consultorio), escalamientos, lecturas de carnet, borradores de autorización y avisos automáticos. La exportación queda en la bitácora.
- **Borrar todo** (`POST /api/panel/pacientes/{id}/borrar` con `{"confirmacion":"BORRAR"}`): elimina al paciente, sus conversaciones y mensajes, permisos, carnets, escalamientos, citas, lista de espera, ofertas, borradores de autorización y mensajes de canal, en una transacción. La bitácora conserva solo las acciones (quién, qué, cuándo), con sesión, entidad, detalle y textos borrados, más un registro `datos_borrados` sin datos personales.

Si el paciente se lo pide al agente, este escala a la secretaria y comparte el aviso de privacidad (regla 16 del prompt).

## Sin datos de pacientes en logs, informes ni errores

- Los ids de sesión de WhatsApp contienen el número (`wa-8095550101`). Fuera de la base se usa un seudónimo estable (`s-3fa2…`): en el scope `SesionId` de los logs, en el registro de uso del modelo y en los logs de error (`Seguridad/Privacidad.cs`).
- Los cuerpos de error de Meta y WhatsApp, los errores de entrega y las excepciones se registran enmascarados: toda secuencia de 7 o más dígitos pasa a `[número]` y los correos a `[correo]`. Las excepciones no controladas las registra `ManejadorErroresPrivado` ya enmascaradas (el log de ASP.NET con la excepción completa está apagado).
- Los argumentos y resultados de las herramientas nunca se registran (llevan datos del paciente).
- El informe de evaluación solo cita ids de casos y cifras; las respuestas citadas en los fallos se enmascaran.

Esto está probado, no solo prometido:

| Prueba | Qué comprueba |
|---|---|
| `PrivacidadTests.Los_logs_de_un_flujo_completo_no_llevan_telefonos_ni_texto_de_pacientes` | Captura cada línea de log (mensaje, excepción y valores del scope) de un chat web, una conversación de WhatsApp donde el modelo falla con un número en la excepción, un escalamiento y un recordatorio, y busca teléfonos, una cédula y el texto del paciente |
| `EvalTests.Todos_los_casos_pasan_con_el_modelo_falso` | El informe generado no tiene teléfonos ni mensajes de pacientes |
| `DerechosDelPacienteTests` | Contenido de la exportación, alcance del borrado, bitácora anonimizada, otros pacientes intactos |

## Seguridad básica

HTTPS en producción (HSTS), panel con cookies seguras, claves con hash y bloqueo por intentos, límites de uso, cabeceras de seguridad, claves solo en variables de entorno y verificación de la firma del webhook de WhatsApp en cada petición. Ver el README.
