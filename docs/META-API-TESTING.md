# Testing against the Meta Model API

*Versión en español: [es/PRUEBAS-META.md](es/PRUEBAS-META.md)*

All the code is done and tested with fake models. With the real key, what is left is confirming that the formats match the documentation (https://dev.meta.ai/docs) and tuning the prompt. Anything that might differ is in configuration, not code, except where noted.

## 1. Quick check with curl

```bash
export META_API_KEY="your-key"
./scripts/probar-meta.sh                       # models + chat with tool calling
./scripts/probar-meta.sh note.ogg card.jpg     # + transcription + reading a fictional card
```

## 2. Automated tests against the real API

They skip themselves when there is no key. With a key:

```bash
META_API_KEY="your-key" dotnet test --filter "Categoria=Meta"
# Optional: DPM_AUDIO_PRUEBA=note.ogg  DPM_IMAGEN_CARNET=fictional-card.jpg
```

| Test | What it confirms |
|---|---|
| `Chat_simple_responde` | Base URL, authentication and the shape of `choices[0].message` |
| `Tool_calling_identifica_y_busca_horarios` | Muse Spark returns `tool_calls` in the expected format and chains tools |
| `Pregunta_clinica_se_escala_sin_consejo_medico` | Safety rule: escalates and gives no dose |
| `Posible_emergencia_indica_911_y_escala_urgente` | Emergency rule |
| `Transcripcion_acepta_el_formato_de_la_peticion` | Muse Voice Transcribe path and fields |
| `Lectura_de_carnet_con_imagen_y_salida_estructurada` | Image as `image_url` + `response_format` with `json_schema` |

The full evaluation bank (`eval/`, 144 cases in 5 languages) runs against the real model with `dotnet run --project tools/DpmAgent.Eval -- --modelo real`.

## 3. If something does not match

| Symptom | Where to adjust |
|---|---|
| 404 on chat | `Meta__BaseUrl` (default `https://api.meta.ai/v1/`) |
| 400 because of `max_completion_tokens` | `MetaModelClient.ParametroMaxTokens` (`src/DpmAgent.Api/Model/MetaModelClient.cs`) |
| The model has another name | `Meta__Model` |
| 404 on transcription | `Transcripcion__Ruta` (default `audio/transcriptions`) |
| Transcription expects other fields or answers differently | `MetaTranscriber.TranscribirAsync` (`src/DpmAgent.Api/Voz/MetaTranscriber.cs`): fields `file`, `model` and `language` (only if `Transcripcion__Idioma` is set; by default the service detects the language), and reading `text` / `duration` |
| The image goes in another format or `json_schema` is not accepted | `MetaLectorCarnet.LeerAsync` (`src/DpmAgent.Api/Carnet/MetaLectorCarnet.cs`) |
| The model does not follow a rule well | System prompt in `ReceptionAgent.SystemPrompt` (`src/DpmAgent.Api/Agent/ReceptionAgent.cs`) |

`-contributor` variants are blocked on purpose: they use the prompts for training.

## 4. Local end-to-end demo

```bash
export META_API_KEY="your-key"
export Panel__ClaveInicial="a-long-password"
dotnet run --project src/DpmAgent.Api
```

- Patient chat: `http://localhost:5271/` (simulated number 809-555-0101 = María, fictional). Text, 🎤 voice note and 📷 card photo.
- Clinic dashboard: `http://localhost:5271/panel.html` (user `secretaria`).
- To see the waiting list in action, open **two chat windows**:
  1. Window A, number 809-555-0101 (María): book Tuesday at 9:00.
  2. Window B, number 829-555-0102 (José): ask for Tuesday at 9:00; since it is taken, accept the waiting list.
  3. Window A: cancel the appointment and confirm.
  4. Within seconds, window B shows the offer for the freed slot; José replies "yes" and it is booked.
