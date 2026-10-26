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
- **Notas de voz**: `POST /api/chat/audio` recibe el audio (OGG/Opus de WhatsApp, WebM, MP3, M4A o WAV), lo transcribe con Muse Voice Transcribe y se lo pasa al agente. El formato se valida por los bytes, hay límite de tamaño y de duración, y el audio nunca se guarda. El panel tiene un botón para grabar desde el navegador.
- **Carnet de la ARS por foto**: `POST /api/pacientes/carnet` lee nombre, ARS y número de afiliado con Muse Spark (imagen + salida estructurada con JSON schema). El resultado queda **pendiente** hasta que el paciente lo confirma en el chat (`confirmar_datos_carnet`) o la secretaria lo revisa (`GET /api/carnets`, `POST /api/carnets/{id}/confirmar` con correcciones opcionales, `POST /api/carnets/{id}/descartar`). Si el número no tenía paciente, se registra con esos datos. La imagen no se guarda; lo leído se limpia como dato no confiable y, si el nombre no coincide con el paciente del número, solo la secretaria puede confirmarlo.
- **Persistencia y multi-consultorio**: citas, pacientes, escalamientos y conversaciones se guardan con EF Core (SQLite en local, PostgreSQL en la nube). Cada registro lleva su `ConsultorioId` y cada consulta se filtra por el consultorio actual.
- **Escalamiento al personal**: preguntas clínicas, quejas o posibles emergencias se pasan a un humano y quedan registradas en `/api/escalamientos`. En una posible emergencia el agente indica llamar al 911.
- Panel de prueba que muestra, al lado del chat, cada herramienta que el modelo usó y lo que devolvió.

## Cómo usa Meta AI

| Pieza | Modelo / producto |
|---|---|
| Razonamiento y tool calling | **Muse Spark** vía Meta Model API (`/v1/chat/completions`) |
| Lectura de carnet de ARS | Muse Spark, entrada de imagen + salida estructurada (`/api/pacientes/carnet`) |
| Transcripción de notas de voz | Muse Voice Transcribe (`/api/chat/audio`) |
| Ejecución local, sin enviar datos fuera del consultorio (planificado) | **Muse Glimmer**, pesos abiertos |

El modelo está detrás de la interfaz `IChatModel`, así que cambiar entre la API de Meta y un servidor propio con Muse Glimmer es cuestión de configuración.

## Arquitectura

```
Navegador / WhatsApp
        │
        ▼
 ASP.NET Core 8 (Minimal API)
   POST /api/chat        POST /api/chat/audio ──► ITranscriber ──► Muse Voice Transcribe
        │                        │ (texto transcrito)
        ├────────────────────────┘
   ReceptionAgent ──► IChatModel ──► Meta Model API (Muse Spark)
        │                             o servidor propio (Muse Glimmer)
        ▼
   AgentTools (9 herramientas administrativas)
        │
        ▼
   IDpmGateway ──► DbDpmGateway (EF Core: SQLite / PostgreSQL)
        │          MockDpmGateway (en memoria, para pruebas)
   IConversationStore ──► DbConversationStore / InMemoryConversationStore
```

```
src/DpmAgent.Api/
  Agent/   ReceptionAgent, AgentTools, Sesion, IConversationStore (memoria y base de datos)
  Data/    DpmDbContext, entidades, migraciones por proveedor, datos de demo
  Model/   IChatModel, MetaModelClient, MetaOptions
  Carnet/  ILectorCarnet, MetaLectorCarnet, ServicioCarnet, endpoints del carnet
  Voz/     ITranscriber, MetaTranscriber, validación de audio, endpoint de notas de voz
  Dpm/     IDpmGateway, DbDpmGateway, MockDpmGateway, horario del consultorio
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

Al arrancar, la app aplica las migraciones y crea `dpm-agent.db` (SQLite) con un consultorio, dos médicos y dos pacientes ficticios. Las citas y conversaciones se conservan entre reinicios.

Pruebas:

```bash
dotnet test
```

La prueba contra PostgreSQL solo corre si se define `DPM_TEST_POSTGRES` con la cadena de una base desechable (el CI la define con un contenedor de Postgres).

Configuración opcional (`appsettings.json` o variables de entorno):

| Variable | Valor por defecto |
|---|---|
| `Meta__Model` | `muse-spark-1.2` |
| `Meta__BaseUrl` | `https://api.meta.ai/v1/` |
| `Meta__Temperature` | `0.2` |
| `Transcripcion__Modelo` | `muse-voice-transcribe-1.0` |
| `Transcripcion__Ruta` | `audio/transcriptions` (relativa a `Meta__BaseUrl`) |
| `Transcripcion__MaxBytes` | `2097152` (2 MB) |
| `Transcripcion__MaxDuracionSegundos` | `120` |
| `Carnet__Modelo` | vacío (usa `Meta__Model`) |
| `Carnet__MaxBytes` | `4194304` (4 MB) |
| `BaseDatos__Proveedor` | `Sqlite` (también `Postgres` o `Memoria`) |
| `ConnectionStrings__Dpm` | `Data Source=dpm-agent.db` (en la nube, la cadena de PostgreSQL por variable de entorno) |
| `BaseDatos__MigrarAlIniciar` | `true` |
| `BaseDatos__SembrarDatosDemo` | `true` |
| `BaseDatos__ConsultorioId` | `1` |

Para generar una migración nueva (una por proveedor):

```bash
dotnet tool restore
dotnet ef migrations add Nombre --project src/DpmAgent.Api --context SqliteDpmDbContext --output-dir Data/Migraciones/Sqlite
dotnet ef migrations add Nombre --project src/DpmAgent.Api --context PostgresDpmDbContext --output-dir Data/Migraciones/Postgres
```

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
