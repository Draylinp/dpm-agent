# DPM Agent

Agente de recepción para consultorios médicos en República Dominicana, construido sobre los modelos **Muse de Meta**.

El agente atiende a los pacientes por chat: busca o registra al paciente, consulta la agenda del médico, ofrece horarios, crea, reprograma y cancela citas, y pasa al personal humano todo lo que no le corresponde. **Es estrictamente administrativo: no da orientación médica.**

> Proyecto para el **Meta AI Developer Hackathon** (26 oct – 4 nov 2026), track Indie Developer.

---

## El problema

En muchos consultorios dominicanos la agenda se maneja por WhatsApp y por teléfono. La secretaria responde los mismos mensajes todo el día: "¿hay cupo mañana?", "¿la doctora atiende el sábado?", "quiero cambiar mi cita". Mientras tanto atiende a los pacientes que están en la sala, llena formularios de las ARS y cobra. Los mensajes se quedan sin responder, se pierden pacientes y se cometen errores de doble reserva.

DPM Agent se encarga de esa conversación repetitiva y deja al personal lo que de verdad necesita a una persona.

## Qué hace hoy

- Conversación en español dominicano, breve, como un mensaje de WhatsApp.
- **Tool calling de varios pasos**: el modelo decide qué herramientas usar y en qué orden (buscar paciente → ver disponibilidad → confirmar → crear cita).
- Validación en el servidor: no se puede reservar un horario ocupado ni en el pasado, aunque el modelo lo intente.
- **Escalamiento al personal**: preguntas clínicas, quejas o posibles emergencias se pasan a un humano y quedan registradas en `/api/escalamientos`. En una posible emergencia el agente indica llamar al 911.
- Panel de prueba que muestra, al lado del chat, cada herramienta que el modelo usó y lo que devolvió.

## Cómo usa Meta AI

| Pieza | Modelo / producto |
|---|---|
| Razonamiento y tool calling | **Muse Spark** vía Meta Model API (`/v1/chat/completions`) |
| Lectura de carnet de ARS (planificado) | Muse Spark, entrada de imagen |
| Transcripción de notas de voz (planificado) | Muse Voice Transcribe |
| Ejecución local, sin enviar datos fuera del consultorio (planificado) | **Muse Glimmer**, pesos abiertos |

El modelo está detrás de la interfaz `IChatModel`, así que cambiar entre la API de Meta y un servidor propio con Muse Glimmer es cuestión de configuración.

## Arquitectura

```
Navegador / WhatsApp
        │
        ▼
 ASP.NET Core 8 (Minimal API)
   POST /api/chat
        │
   ReceptionAgent ──► IChatModel ──► Meta Model API (Muse Spark)
        │                             o servidor propio (Muse Glimmer)
        ▼
   AgentTools (8 herramientas administrativas)
        │
        ▼
   IDpmGateway ──► MockDpmGateway (datos ficticios)
                   └─► API de DPM (integración futura)
```

```
src/DpmAgent.Api/
  Agent/   ReceptionAgent, AgentTools, ConversationStore
  Model/   IChatModel, MetaModelClient, MetaOptions
  Dpm/     IDpmGateway, MockDpmGateway
  wwwroot/ panel de prueba
tests/DpmAgent.Tests/   pruebas con un modelo simulado (sin llamadas reales)
```

## Ejecutar en local

Requisitos: .NET 8 SDK y una clave de la Meta Model API.

```bash
git clone https://github.com/Draylinp/dpm-agent.git
cd dpm-agent

# Linux / macOS
export META_API_KEY="tu-clave"
# Windows PowerShell
# $env:META_API_KEY="tu-clave"

dotnet run --project src/DpmAgent.Api
```

Abre la URL que muestra la consola (por ejemplo `http://localhost:5271`). El campo *Número de WhatsApp (simulado)* hace de remitente: con `809-555-0101` el agente te reconoce como la paciente ficticia María Rodríguez. Escribe algo como:
*"Hola, quiero una cita con la doctora para mañana en la mañana."*

Pruebas:

```bash
dotnet test
```

Configuración opcional (`appsettings.json` o variables de entorno):

| Variable | Valor por defecto |
|---|---|
| `Meta__Model` | `muse-spark-1.2` |
| `Meta__BaseUrl` | `https://api.meta.ai/v1/` |
| `Meta__Temperature` | `0.2` |

## Seguridad y límites

- **Cada conversación solo actúa sobre su propio paciente.** El paciente se identifica por el teléfono que entrega el canal (el remitente de WhatsApp), nunca por lo que diga el modelo. Las herramientas de citas toman el paciente de la sesión, y cancelar una cita ajena devuelve el mismo error que una cita que no existe. Una sesión no puede cambiar de número.
- **Sin orientación médica.** El prompt de sistema lo prohíbe y no existe ninguna herramienta clínica. Cualquier pregunta de salud se escala al personal.
- **Solo datos ficticios.** Todos los pacientes y médicos del repositorio son inventados.
- El motivo de la cita se guarda en términos administrativos ("control", "primera consulta"), sin detalles de salud.
- Las claves se leen de variables de entorno y nunca se guardan en el repositorio.

## Qué existía antes del evento y qué se construyó durante

**Antes del 26 de octubre de 2026:** DPM, mi sistema de gestión de consultorio (repositorio privado aparte). Ese sistema **no** forma parte de esta entrega ni del demo.

**Durante el hackathon (todo lo que hay en este repositorio):** el agente, el cliente de la Meta Model API, las herramientas, la simulación de DPM, el panel de prueba y las pruebas. El historial de commits empieza el 26 de octubre de 2026.

## Licencia

MIT
