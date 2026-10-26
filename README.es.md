# DPM Agent

*English version: [README.md](README.md)*

Agente de recepción para consultorios médicos en República Dominicana, construido sobre los modelos **Muse de Meta**.

El agente atiende a los pacientes por chat: busca o registra al paciente, consulta la agenda del médico, ofrece horarios, crea, reprograma y cancela citas, y pasa al personal humano todo lo que no le corresponde. **Es estrictamente administrativo: no da orientación médica.**

> Proyecto para el **Meta AI Developer Hackathon** (26 oct – 4 nov 2026), track Indie Developer.

---

## El problema

En muchos consultorios dominicanos la agenda se maneja por WhatsApp y por teléfono. La secretaria responde los mismos mensajes todo el día: "¿hay cupo mañana?", "¿la doctora atiende el sábado?", "quiero cambiar mi cita". Mientras tanto atiende a los pacientes que están en la sala, llena formularios de las ARS y cobra. Los mensajes se quedan sin responder, se pierden pacientes y se cometen errores de doble reserva.

DPM Agent se encarga de esa conversación repetitiva y deja al personal lo que de verdad necesita a una persona.

## Qué hace hoy

- **Multilingüe**: español, inglés y kreyòl ayisyen garantizados (textos fijos, plantillas de WhatsApp y pruebas en los tres), y cualquier otro idioma con respaldo en inglés. El idioma se detecta en cada mensaje y la conversación lo sigue si el paciente cambia. La interfaz y el panel tienen selector español/inglés.
- Conversación breve y cordial, como un mensaje de WhatsApp.
- **Tool calling de varios pasos**: el modelo decide qué herramientas usar y en qué orden (buscar paciente → ver disponibilidad → confirmar → crear cita).
- Validación en el servidor: no se puede reservar un horario ocupado ni en el pasado, aunque el modelo lo intente.
- **Notas de voz**: `POST /api/chat/audio` recibe el audio (OGG/Opus de WhatsApp, WebM, MP3, M4A o WAV), lo transcribe con Muse Voice Transcribe y se lo pasa al agente. El formato se valida por los bytes, hay límite de tamaño y de duración, y el audio nunca se guarda. El panel tiene un botón para grabar desde el navegador.
- **Carnet de la ARS por foto**: `POST /api/pacientes/carnet` lee nombre, ARS y número de afiliado con Muse Spark (imagen + salida estructurada con JSON schema). El resultado queda **pendiente** hasta que el paciente lo confirma en el chat (`confirmar_datos_carnet`) o la secretaria lo revisa (`GET /api/carnets`, `POST /api/carnets/{id}/confirmar` con correcciones opcionales, `POST /api/carnets/{id}/descartar`). Si el número no tenía paciente, se registra con esos datos. La imagen no se guarda; lo leído se limpia como dato no confiable y, si el nombre no coincide con el paciente del número, solo la secretaria puede confirmarlo.
- **Lista de espera que llena huecos sola**: si no hay horario, el agente anota al paciente (`agregar_lista_espera`). Cuando alguien cancela, un servicio en segundo plano ofrece el hueco al primero de la lista cuyo rango de días y horas coincide, por su canal. Si no responde en 30 minutos, la oferta vence y pasa al siguiente; si acepta (`responder_oferta`), se reserva validando de nuevo que siga libre.
- **Recordatorio 24 horas antes**, con opción de confirmar (`confirmar_asistencia`) o cancelar.
- **Bitácora** (`GET /api/bitacora`): quién hizo qué y cuándo (agente, sistema o secretaria), sin detalles clínicos. Los mensajes automáticos son plantillas fijas, no texto del modelo.
- **WhatsApp Cloud API**: webhook con verificación de firma `X-Hub-Signature-256`, texto, notas de voz y fotos del carnet, respuestas y mensajes proactivos con plantillas aprobadas. Ver [docs/es/WHATSAPP.md](docs/es/WHATSAPP.md).
- **Panel de la secretaria** (`/panel.html`): inicio de sesión con cookie segura (`HttpOnly`, `SameSite=Strict`), claves con `PasswordHasher` y bloqueo tras 5 intentos. Muestra las citas del día, los escalamientos (urgentes primero, con botón de atendido), los carnets por confirmar y la bitácora. Cada secretaria solo ve los datos de su consultorio.
- **Borrador de autorización de ARS**: desde las citas del día, la secretaria pide un borrador con los datos administrativos (ARS, afiliado, médico, especialidad, fecha). Lo que falta queda marcado como `[FALTA: …]` y no se puede aprobar hasta completarlo. Aprobar solo lo marca como listo: **nunca se envía solo**.
- **Persistencia y multi-consultorio**: citas, pacientes, escalamientos y conversaciones se guardan con EF Core (SQLite en local, PostgreSQL en la nube). Cada registro lleva su `ConsultorioId` y cada consulta se filtra por el consultorio actual.
- **Escalamiento al personal**: preguntas clínicas, quejas o posibles emergencias se pasan a un humano y quedan registradas en `/api/escalamientos`. En una posible emergencia el agente indica llamar al número de emergencias del consultorio (911 por defecto).
- Panel de prueba que muestra, al lado del chat, cada herramienta que el modelo usó y lo que devolvió.

## Cómo usa Meta AI

| Pieza | Modelo / producto |
|---|---|
| Razonamiento y tool calling | **Muse Spark** vía Meta Model API (`/v1/chat/completions`) |
| Lectura de carnet de ARS | Muse Spark, entrada de imagen + salida estructurada (`/api/pacientes/carnet`) |
| Transcripción de notas de voz | Muse Voice Transcribe (`/api/chat/audio`) |
| Ejecución en servidor propio o como respaldo | **Muse Glimmer**, pesos abiertos, servido con vLLM (Modal, Baseten o local) |

El modelo está detrás de `IChatModel`: `Modelo__Proveedor` elige entre la Meta Model API y un servidor compatible con OpenAI, y `Modelo__Respaldo` define a cuál pasar si el principal falla. Ver [docs/es/GLIMMER.md](docs/es/GLIMMER.md).

## Evaluación

84 casos en 11 categorías (agendar, reprogramar, cancelar, paciente nuevo, carnet, lista de espera, pregunta clínica, emergencia, prompt injection, cita ajena y cambio de idioma) en español, inglés y kreyòl, más francés y portugués de nivel 2. Dos capas: comprobaciones deterministas sobre herramientas y estado de la base, y un juez LLM (Muse Glimmer por defecto) con una rúbrica escrita. Ver [docs/es/EVALUACION.md](docs/es/EVALUACION.md).

**Contra Muse Spark:**

<!-- RESULTADOS MUSE SPARK: correr `dotnet run --project tools/DpmAgent.Eval -- --modelo real` y pegar aquí las tablas de eval/resultados/ -->
*Pendiente: requiere acceso a la Meta Model API.*

**Garantías del servidor (modelo guionado, corre en el CI en cada push):** 84 de 84 casos pasan — [último informe](eval/resultados/2026-10-26-modelo-falso.md). Algunos guiones hacen de un modelo que obedece una inyección a propósito; el servidor igual impide cancelar citas ajenas o cancelar sin que el paciente confirme.

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
   AgentTools (12 herramientas administrativas)
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
  Agenda/  lista de espera, motor de ofertas y recordatorios, notificador, despachador, bitácora
  Autorizaciones/ borradores de autorización de ARS
  Canales/ IMessageChannel, WebChatChannel, WhatsApp/ (Cloud API, webhook, procesador)
  Carnet/  ILectorCarnet, MetaLectorCarnet, ServicioCarnet, endpoints del carnet
  Panel/   usuarios, autenticación por cookie, vistas de la secretaria
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

Con la clave de Meta, `./scripts/probar-meta.sh` y `dotnet test --filter "Categoria=Meta"` confirman los formatos reales. Ver [docs/es/PRUEBAS-META.md](docs/es/PRUEBAS-META.md).

La prueba contra PostgreSQL solo corre si se define `DPM_TEST_POSTGRES` con la cadena de una base desechable (el CI la define con un contenedor de Postgres).

Despliegue en Azure App Service con PostgreSQL, Key Vault y GitHub Actions: ver [docs/es/DESPLIEGUE.md](docs/es/DESPLIEGUE.md). También hay un `Dockerfile` multi-stage.

Configuración opcional (`appsettings.json` o variables de entorno):

| Variable | Valor por defecto |
|---|---|
| `Meta__Model` | `muse-spark-1.2` (las variantes `-contributor` se rechazan al arrancar) |
| `Modelo__Proveedor` | `MetaApi` u `OpenAICompatible` |
| `Modelo__Respaldo` | vacío, o el otro proveedor |
| `Meta__BaseUrl` | `https://api.meta.ai/v1/` |
| `Meta__Temperature` | `0.2` |
| `Transcripcion__Modelo` | `muse-voice-transcribe-1.0` |
| `Transcripcion__Ruta` | `audio/transcriptions` (relativa a `Meta__BaseUrl`) |
| `Transcripcion__MaxBytes` | `2097152` (2 MB) |
| `Transcripcion__MaxDuracionSegundos` | `120` |
| `Carnet__Modelo` | vacío (usa `Meta__Model`) |
| `Carnet__MaxBytes` | `4194304` (4 MB) |
| `BaseDatos__Proveedor` | `Sqlite` (también `Postgres`, o `Memoria` = SQLite en memoria) |
| `Panel__ClaveInicial` | Clave del usuario inicial `secretaria` (administrador). En desarrollo, si falta, se genera una y se muestra en la consola |
| `Agenda__MotorActivo` | `true` (también despacha los mensajes salientes) |
| `Agenda__IntervaloSegundos` | `15` |
| `Agenda__OfertaMinutos` | `30` |
| `Agenda__AnticipacionMinimaMinutos` | `60` |
| `Agenda__RecordatorioHorasAntes` | `24` |
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

- **Defensa contra prompt injection validada en el servidor**: lo que devuelven las herramientas, las transcripciones y lo leído en fotos se tratan como datos. Cancelar una cita es en dos pasos que controla el servidor: solo se ejecuta en un turno posterior, o sea, después de un mensaje real del paciente. Un turno no puede hacer más de 3 acciones que cambien datos ni más de 5 herramientas por respuesta. El motivo de la cita solo admite valores administrativos.
- **Rate limiting** por IP (endpoints públicos y login) y por conversación (todos los canales).
- **Logs estructurados** (JSON en producción) con `SesionId` y `CorrelationId`, sin argumentos ni resultados de herramientas: no llevan datos de pacientes.
- **Retención**: las conversaciones se borran a los 30 días (configurable); la bitácora se guarda un año.
- **Salud**: `/health/live` y `/health/ready` (base de datos y modelo configurado).
- Cabeceras de seguridad (CSP, `X-Frame-Options`, `nosniff`), HSTS y HTTPS en producción.

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
