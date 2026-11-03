# Probar con la Meta Model API

*English version: [../META-API-TESTING.md](../META-API-TESTING.md)*

Todo el código ya está hecho y probado con modelos falsos. Con la clave real solo queda confirmar que los formatos coinciden con la documentación (https://dev.meta.ai/docs) y ajustar el prompt. Lo que podría cambiar está en la configuración, no en el código, salvo donde se indica.

## 1. Prueba rápida con curl

```bash
export META_API_KEY="tu-clave"
./scripts/probar-meta.sh                       # modelos + chat con tool calling
./scripts/probar-meta.sh nota.ogg carnet.jpg   # + transcripción + lectura de carnet ficticio
```

## 2. Pruebas automáticas contra la API real

Se saltan solas si no hay clave. Con clave:

```bash
META_API_KEY="tu-clave" dotnet test --filter "Categoria=Meta"
# Opcionales: DPM_AUDIO_PRUEBA=nota.ogg  DPM_IMAGEN_CARNET=carnet-ficticio.jpg
```

| Prueba | Qué confirma |
|---|---|
| `Chat_simple_responde` | URL base, autenticación y forma de `choices[0].message` |
| `Tool_calling_identifica_y_busca_horarios` | Que Muse Spark devuelve `tool_calls` en el formato esperado y encadena herramientas |
| `Pregunta_clinica_se_escala_sin_consejo_medico` | Regla de seguridad: escala y no da dosis |
| `Posible_emergencia_indica_911_y_escala_urgente` | Regla de emergencia |
| `Transcripcion_acepta_el_formato_de_la_peticion` | Ruta y campos de Muse Voice Transcribe |
| `Lectura_de_carnet_con_imagen_y_salida_estructurada` | Imagen como `image_url` + `response_format` con `json_schema` |

## 3. Si algo no coincide

| Síntoma | Dónde se ajusta |
|---|---|
| 404 en el chat | `Meta__BaseUrl` (por defecto `https://api.meta.ai/v1/`) |
| 400 por `max_completion_tokens` | `MetaModelClient.ParametroMaxTokens` (`src/DpmAgent.Api/Model/MetaModelClient.cs`) |
| El modelo no se llama así | `Meta__Model` |
| 404 en la transcripción | `Transcripcion__Ruta` (por defecto `audio/transcriptions`) |
| La transcripción pide otros campos o responde distinto | `MetaTranscriber.TranscribirAsync` (`src/DpmAgent.Api/Voz/MetaTranscriber.cs`): nombres `file`, `model` y `language` (este solo si se configura `Transcripcion__Idioma`; por defecto el servicio detecta el idioma) y lectura de `text` / `duration` |
| La imagen va en otro formato o no se acepta `json_schema` | `MetaLectorCarnet.LeerAsync` (`src/DpmAgent.Api/Carnet/MetaLectorCarnet.cs`) |
| El modelo no sigue bien una regla | Prompt de sistema en `ReceptionAgent.SystemPrompt` (`src/DpmAgent.Api/Agent/ReceptionAgent.cs`) |

Las variantes `-contributor` están bloqueadas a propósito: usan los prompts para entrenar.

## 4. Demo de punta a punta en local

```bash
export META_API_KEY="tu-clave"
export Panel__ClaveInicial="una-clave-larga"
dotnet run --project src/DpmAgent.Api
```

- Chat del paciente: `http://localhost:5271/` (número simulado 809-555-0101 = María, ficticia). Texto, 🎤 nota de voz y 📷 foto del carnet.
- Panel de la secretaria: `http://localhost:5271/panel.html` (usuario `secretaria`).
- Para ver la lista de espera en acción, abre **dos ventanas** del chat:
  1. Ventana A, número 809-555-0101 (María): reserva el martes a las 9:00.
  2. Ventana B, número 829-555-0102 (José): pide el martes a las 9:00; como está ocupado, acepta la lista de espera.
  3. Ventana A: cancela la cita y confirma.
  4. En unos segundos aparece en la ventana B la oferta del turno liberado; José responde "sí" y queda reservado.
