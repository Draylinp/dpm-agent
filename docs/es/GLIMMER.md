# Modo con pesos abiertos: Muse Glimmer

*English version: [../GLIMMER.md](../GLIMMER.md)*

El agente puede usar **Muse Spark** por la Meta Model API o **Muse Glimmer** (pesos abiertos, Apache 2.0) en un servidor propio. Así el consultorio puede correr el modelo sin que las conversaciones salgan a un tercero, o tenerlo como **respaldo** si la API no responde.

## Configuración en la app

| Variable | Valor |
|---|---|
| `Modelo__Proveedor` | `MetaApi` (por defecto) u `OpenAICompatible` |
| `Modelo__Respaldo` | Vacío, o el otro proveedor para usarlo si el principal falla |
| `OpenAICompatible__BaseUrl` | URL del servidor terminada en `/v1/` |
| `OpenAICompatible__Model` | Nombre del modelo en el servidor (en vLLM, el id de Hugging Face) |
| `OpenAICompatible__ApiKey` | La clave del servidor (`--api-key` de vLLM). Vacía si no pide |
| `OpenAICompatible__TimeoutSeconds` | `120` (el arranque en frío de una GPU tarda) |

El respaldo entra solo si el principal **no está disponible**: sin clave, error de red, tiempo agotado, 429 o 5xx. Un 400 o 401 no se reintenta.

Por privacidad, la app **no arranca** si algún modelo configurado es una variante `-contributor` (Meta usa esos prompts para entrenar).

## Opción A: Modal (recomendado para el demo)

Glimmer pesa unos 18 GB cuantizado a 4 bits; una **L40S** (48 GB) deja espacio para el contexto.

1. Instala Modal y autentícate: `pip install modal && modal setup`.
2. Busca en Hugging Face el repositorio exacto de Muse Glimmer (la versión cuantizada si vas a usar 4 bits) y el **parser de tool calling** de vLLM que indique su model card.
3. Crea el secret (nada de esto va al repositorio):
   ```bash
   modal secret create dpm-glimmer \
     VLLM_API_KEY="$(openssl rand -hex 32)" \
     GLIMMER_MODEL_ID="<repo de Hugging Face>" \
     GLIMMER_TOOL_PARSER="<parser>" \
     HF_TOKEN="<token si hace falta>"
   ```
   Si el repositorio es una versión cuantizada que vLLM no detecta sola, agrega `GLIMMER_QUANTIZATION=awq` (o el método que corresponda).
4. Despliega (opcionalmente fija versiones: `VLLM_VERSION=... GLIMMER_GPU=L40S`):
   ```bash
   modal deploy deploy/modal/glimmer_vllm.py
   ```
5. Prueba el endpoint (la primera vez descarga los pesos; puede tardar varios minutos):
   ```bash
   curl -H "Authorization: Bearer $VLLM_API_KEY" https://<tu-usuario>--dpm-glimmer-serve.modal.run/v1/models
   ```
6. Configura la app:
   ```bash
   Modelo__Proveedor=OpenAICompatible
   OpenAICompatible__BaseUrl=https://<tu-usuario>--dpm-glimmer-serve.modal.run/v1/
   OpenAICompatible__Model=<repo de Hugging Face>
   OpenAICompatible__ApiKey=<la VLLM_API_KEY>
   ```
   O deja Muse Spark como principal y Glimmer como `Modelo__Respaldo=OpenAICompatible`.

El contenedor se apaga tras 15 minutos sin uso (`scaledown_window`); el siguiente mensaje lo vuelve a encender.

## Opción B: Baseten

Despliega el modelo con un *Truss* de vLLM (Baseten tiene plantillas para modelos de Hugging Face) con las mismas banderas: `--enable-auto-tool-choice --tool-call-parser <parser>`. Baseten expone un endpoint compatible con OpenAI; usa su URL como `OpenAICompatible__BaseUrl` y la API key de Baseten como `OpenAICompatible__ApiKey`.

## Opción C: en el propio consultorio

Con una GPU de 24 GB o más:

```bash
vllm serve <repo de Hugging Face> --port 8000 --api-key <clave> \
  --enable-auto-tool-choice --tool-call-parser <parser> --max-model-len 16384
```

y `OpenAICompatible__BaseUrl=http://localhost:8000/v1/`.

Con **Ollama**, si hay una versión del modelo para Ollama: `ollama run <nombre>` y `OpenAICompatible__BaseUrl=http://localhost:11434/v1/`, `OpenAICompatible__Model=<nombre>` (sin clave).

## Qué sigue usando la Meta Model API

La transcripción de notas de voz (Muse Voice Transcribe) y la lectura del carnet (Muse Spark con imagen) siguen yendo a la Meta Model API. El modo local cubre la conversación y el tool calling, que es donde viaja la mayor parte de los datos.
