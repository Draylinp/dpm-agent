# Roadmap del hackathon

Entrega: **miércoles 4 de noviembre, 9:00 PM ET** (misma hora en República Dominicana).

| Día | Objetivo | Estado |
|---|---|---|
| Lun 26 oct | Base del proyecto: API, cliente Meta, agente con tool calling, DPM simulado, pruebas | ✅ |
| Lun 26 oct | Seguridad: cada sesión queda atada al paciente del teléfono del canal; no se pueden ver, crear ni cancelar citas ajenas | ✅ |
| Lun 26 oct | F1: persistencia con EF Core (SQLite local, PostgreSQL en la nube), entidades con `ConsultorioId`, seed ficticio, historial persistente; se conserva todo al reiniciar | ✅ |
| Lun 26 oct | Primera llamada real a Muse Spark con la clave del evento | ⬜ |
| Mar 27 oct | Workshop Meta P1. Ajustar prompt con conversaciones reales de prueba | ⬜ |
| Mié 28 oct | Workshop Meta P2. Reprogramar citas y recordatorios | ⬜ |
| Jue 29 oct | Lectura de carnet de ARS con imagen (llenar datos del paciente) | ⬜ |
| Vie 30 oct | Borrador de solicitud de autorización a la ARS | ⬜ |
| Sáb 31 oct – Dom 1 nov | Notas de voz con Muse Voice Transcribe / prueba con Muse Glimmer local | ⬜ |
| Lun 2 nov | Pulir panel, README, despliegue en Azure | ⬜ |
| Mar 3 nov | Grabar video de 3 minutos | ⬜ |
| Mié 4 nov | Enviar temprano. Verificar repo en navegador sin sesión | ⬜ |

## Guion del video (3 minutos)

1. **0:00–0:30** El problema: la secretaria y el WhatsApp lleno.
2. **0:30–2:15** Demo en vivo: el paciente pide cita, el agente busca, ofrece horarios, confirma y crea la cita. Panel de pasos visible. Luego una pregunta clínica que se escala.
3. **2:15–2:45** Cómo usa Meta: Muse Spark con tool calling, imagen y voz.
4. **2:45–3:00** Impacto y siguiente paso.
