# Open-weights mode: Muse Glimmer

*Versión en español: [es/GLIMMER.md](es/GLIMMER.md)*

The agent can use **Muse Spark** through the Meta Model API or **Muse Glimmer** (open weights, Apache 2.0) on your own server. That lets a clinic run the model without conversations leaving to a third party, or keep it as a **fallback** if the API does not answer. Glimmer is also the default judge of the evaluation bank (`eval/`).

## App configuration

| Variable | Value |
|---|---|
| `Modelo__Proveedor` | `MetaApi` (default) or `OpenAICompatible` |
| `Modelo__Respaldo` | Empty, or the other provider to use when the main one fails |
| `OpenAICompatible__BaseUrl` | Server URL ending in `/v1/` |
| `OpenAICompatible__Model` | Model name on the server (in vLLM, the Hugging Face id) |
| `OpenAICompatible__ApiKey` | Server key (vLLM `--api-key`). Empty if not required |
| `OpenAICompatible__TimeoutSeconds` | `120` (a GPU cold start takes time) |

The fallback only kicks in when the main provider is **unavailable**: missing key, network error, timeout, 429 or 5xx. A 400 or 401 is not retried.

For privacy, the app **refuses to start** if any configured model is a `-contributor` variant (Meta uses those prompts for training).

## Option A: Modal (recommended for the demo)

Glimmer is about 18 GB quantized to 4 bits; an **L40S** (48 GB) leaves room for the context.

1. Install Modal and sign in: `pip install modal && modal setup`.
2. Find on Hugging Face the exact Muse Glimmer repository (the quantized version if you use 4 bits) and the vLLM **tool-calling parser** its model card recommends.
3. Create the secret (none of this goes into the repository):
   ```bash
   modal secret create dpm-glimmer \
     VLLM_API_KEY="$(openssl rand -hex 32)" \
     GLIMMER_MODEL_ID="<Hugging Face repo>" \
     GLIMMER_TOOL_PARSER="<parser>" \
     HF_TOKEN="<token if needed>"
   ```
   If the repository is a quantized version vLLM does not detect on its own, add `GLIMMER_QUANTIZATION=awq` (or the matching method).
4. Deploy (optionally pin versions: `VLLM_VERSION=... GLIMMER_GPU=L40S`):
   ```bash
   modal deploy deploy/modal/glimmer_vllm.py
   ```
5. Test the endpoint (the first time it downloads the weights; it can take several minutes):
   ```bash
   curl -H "Authorization: Bearer $VLLM_API_KEY" https://<your-user>--dpm-glimmer-serve.modal.run/v1/models
   ```
6. Configure the app:
   ```bash
   Modelo__Proveedor=OpenAICompatible
   OpenAICompatible__BaseUrl=https://<your-user>--dpm-glimmer-serve.modal.run/v1/
   OpenAICompatible__Model=<Hugging Face repo>
   OpenAICompatible__ApiKey=<the VLLM_API_KEY>
   ```
   Or keep Muse Spark as the main model and Glimmer as `Modelo__Respaldo=OpenAICompatible`.

The container shuts down after 15 idle minutes (`scaledown_window`); the next message starts it again.

## Option B: Baseten

Deploy the model with a vLLM *Truss* (Baseten has templates for Hugging Face models) with the same flags: `--enable-auto-tool-choice --tool-call-parser <parser>`. Baseten exposes an OpenAI-compatible endpoint; use its URL as `OpenAICompatible__BaseUrl` and the Baseten API key as `OpenAICompatible__ApiKey`.

## Option C: inside the clinic

With a GPU of 24 GB or more:

```bash
vllm serve <Hugging Face repo> --port 8000 --api-key <key> \
  --enable-auto-tool-choice --tool-call-parser <parser> --max-model-len 16384
```

and `OpenAICompatible__BaseUrl=http://localhost:8000/v1/`. The `docker-compose.yml` has a `glimmer` profile that does exactly this (see [INSTALLATION.md](INSTALLATION.md)).

With **Ollama**, if there is an Ollama build of the model: `ollama run <name>` and `OpenAICompatible__BaseUrl=http://localhost:11434/v1/`, `OpenAICompatible__Model=<name>` (no key).

## What still uses the Meta Model API

Voice-note transcription (Muse Voice Transcribe) and insurance-card reading (Muse Spark with images) still go to the Meta Model API. Local mode covers the conversation and tool calling, which is where most of the data travels.
