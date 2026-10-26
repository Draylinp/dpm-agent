# Roadmap del hackathon

Entrega: **miércoles 4 de noviembre, 9:00 PM ET** (misma hora en República Dominicana).

| Día | Objetivo | Estado |
|---|---|---|
| Lun 26 oct | Base del proyecto: API, cliente Meta, agente con tool calling, DPM simulado, pruebas | ✅ |
| Lun 26 oct | Seguridad: cada sesión queda atada al paciente del teléfono del canal; no se pueden ver, crear ni cancelar citas ajenas | ✅ |
| Lun 26 oct | F1: persistencia con EF Core (SQLite local, PostgreSQL en la nube), entidades con `ConsultorioId`, seed ficticio, historial persistente; se conserva todo al reiniciar | ✅ |
| Lun 26 oct | F2: notas de voz (`/api/chat/audio`, límites de tamaño y duración, grabación en el panel) | ✅ |
| Lun 26 oct | F3: carnet de la ARS por foto, pendiente de confirmación del paciente o la secretaria, sin guardar la imagen | ✅ |
| Lun 26 oct | F4: lista de espera con ofertas automáticas (vencimiento y siguiente candidato), recordatorio de 24 horas, bitácora y cola de mensajes salientes | ✅ |
| Lun 26 oct | F5: canales web y WhatsApp Cloud API (webhook firmado, texto, voz, imagen, respuestas y plantillas) | ✅ |
| Lun 26 oct | F6: panel de la secretaria con autenticación (citas del día, escalamientos, carnets, bitácora) | ✅ |
| Lun 26 oct | F7: borrador de autorización de ARS (revisar, editar, aprobar; nunca se envía solo) | ✅ |
| Lun 26 oct | F8: modelo intercambiable (Meta API / servidor OpenAI-compatible), respaldo automático, bloqueo de variantes -contributor y guía para Muse Glimmer en Modal | ✅ |
| Lun 26 oct | F9: Dockerfile, despliegue a Azure con OIDC, rate limiting, health checks, logs JSON con correlación, defensa contra prompt injection en el servidor y retención | ✅ |
| Lun 26 oct | F10: multilingüe. Nivel 1 garantizado (es, en, ht): detección por mensaje, idioma en la sesión y el paciente, textos fijos y plantillas en los tres idiomas. Nivel 2 abierto con respaldo en inglés. Número de emergencias e idioma predeterminado por consultorio; escalamientos y bitácora con mensaje original y resúmenes es/en | ✅ |
| Lun 26 oct | F10b: chat y panel con selector español/inglés (recordado en el navegador), resúmenes del panel en el idioma elegido, README y guías en inglés con versión en español (`README.es.md`, `docs/es/`) | ✅ |
| Lun 26 oct | F11: banco de evaluación (84 casos, 89 con F12, 11 categorías, es/en/ht + fr/pt), verificador determinista, juez LLM con rúbrica, evaluador `tools/DpmAgent.Eval` con tablas por categoría e idioma, tokens y latencia p50/p95; corre en CI con el modelo guionado | ✅ |
| Lun 26 oct | F12: filtro de seguridad de salida (reglas multilingües + Llama Guard con pesos abiertos o el modelo como clasificador); reemplaza, escala y registra en la bitácora; medido en el banco (5 casos con dosis bloqueados) | ✅ |
| Lun 26 oct | F13: demo pública (`Demo:Habilitado`): banner, 6 ejemplos en el idioma de la interfaz, traza visible, reinicio aislado al consultorio marcado `EsDemo`, escenario con cita y lista de espera listo para la oferta automática, límite diario por IP y de reinicios por hora | ✅ |
| Lun 26 oct | F14: costo. Tokens de entrada y salida por llamada (sin contenido) guardados en segundo plano con consultorio y conversación; precios por modelo en la configuración; pestaña y endpoint del panel con costo por conversación y por cita agendada; el banco informa el costo promedio por caso | ✅ |
| Lun 26 oct | F15: `docker-compose.yml` (app + PostgreSQL con volúmenes y healthcheck, perfil opcional `glimmer` con vLLM en GPU local), `.env.example` completo y guía de instalación en inglés y español | ✅ |
| Lun 26 oct | F16: README final para los jueces (inglés y español): frase y captura real de la demo, problema con marcadores `<!-- CIFRA PILOTO -->`, tabla de uso de Meta, resultados de evaluación y costo, seguridad, arquitectura en Mermaid, demo, Docker y pruebas | ✅ |
| Pendiente | F16: llenar las cifras del piloto, reemplazar la captura por un GIF de una conversación real y el costo con Muse Spark | ⬜ |
| Pendiente | F11: correr el banco contra Muse Spark y pegar la tabla en el README | ⬜ |
| Lun 26 oct | Pruebas de integración opcionales contra la Meta Model API, script con curl y guía de ajustes | ✅ |
| Mar 27 oct | Workshop Meta P1. Primera llamada real (`docs/es/PRUEBAS-META.md`) y ajuste del prompt con conversaciones de prueba | ⬜ |
| Mié 28 oct | Workshop Meta P2. Confirmar en la documentación y con la clave real Muse Voice Transcribe y la lectura de carnet (imagen + `json_schema`) | ⬜ |
| Jue 29 oct | Conectar un número de prueba de WhatsApp (`docs/es/WHATSAPP.md`) y probar texto, nota de voz, foto y oferta de lista de espera | ⬜ |
| Vie 30 oct | Servir Muse Glimmer en Modal (`docs/es/GLIMMER.md`) y probarlo como principal y como respaldo | ⬜ |
| Sáb 31 oct – Dom 1 nov | Despliegue en Azure (`docs/es/DESPLIEGUE.md`) y prueba de punta a punta en la nube | ⬜ |
| Lun 2 nov | Pulir panel y README con capturas reales | ⬜ |
| Mar 3 nov | Grabar video de 3 minutos | ⬜ |
| Mié 4 nov | Enviar temprano. Verificar repo en navegador sin sesión | ⬜ |

## Guion del video (3 minutos)

1. **0:00–0:30** El problema: la secretaria y el WhatsApp lleno.
2. **0:30–2:15** Demo en vivo por WhatsApp: nota de voz pidiendo cita → el agente identifica, ofrece horarios y reserva. Foto del carnet → confirma la ARS. Otra paciente cancela → la lista de espera ofrece el hueco solo. Pregunta clínica → se escala. Panel de la secretaria con el escalamiento y el borrador de autorización.
3. **2:15–2:45** Cómo usa Meta: Muse Spark (tool calling, imagen con salida estructurada), Muse Voice Transcribe y Muse Glimmer con pesos abiertos como respaldo o modo local.
4. **2:45–3:00** Impacto y siguiente paso.
