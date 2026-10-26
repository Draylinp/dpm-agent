"""
Sirve Muse Glimmer (pesos abiertos) con vLLM en Modal, con un endpoint compatible con OpenAI.

Despliegue:
    modal secret create dpm-glimmer \
        VLLM_API_KEY=<clave-larga-aleatoria> \
        GLIMMER_MODEL_ID=<repo exacto de Hugging Face> \
        GLIMMER_TOOL_PARSER=<parser de tool calling de vLLM para este modelo> \
        HF_TOKEN=<token de Hugging Face, si el repo lo pide>
    modal deploy deploy/modal/glimmer_vllm.py

Opcionales en el mismo secret: GLIMMER_QUANTIZATION (por ejemplo awq), GLIMMER_MAX_MODEL_LEN.
Opcionales al desplegar (variables de entorno locales): GLIMMER_GPU (por defecto L40S), VLLM_VERSION.

La URL que imprime `modal deploy` + "/v1/" es OpenAICompatible__BaseUrl en la app.
"""

import os
import subprocess

import modal

GPU = os.environ.get("GLIMMER_GPU", "L40S")
VLLM_VERSION = os.environ.get("VLLM_VERSION", "")
PUERTO = 8000

imagen = (
    modal.Image.debian_slim(python_version="3.12")
    .pip_install(f"vllm=={VLLM_VERSION}" if VLLM_VERSION else "vllm", "huggingface_hub[hf_transfer]")
    .env({"HF_HUB_ENABLE_HF_TRANSFER": "1"})
)

# Los pesos se descargan una sola vez y quedan en volúmenes persistentes.
cache_hf = modal.Volume.from_name("dpm-glimmer-hf-cache", create_if_missing=True)
cache_vllm = modal.Volume.from_name("dpm-glimmer-vllm-cache", create_if_missing=True)

app = modal.App("dpm-glimmer")


@app.function(
    image=imagen,
    gpu=GPU,
    secrets=[modal.Secret.from_name("dpm-glimmer")],
    volumes={"/root/.cache/huggingface": cache_hf, "/root/.cache/vllm": cache_vllm},
    scaledown_window=15 * 60,
    timeout=10 * 60,
)
@modal.concurrent(max_inputs=16)
@modal.web_server(port=PUERTO, startup_timeout=20 * 60)
def serve():
    modelo = os.environ["GLIMMER_MODEL_ID"]
    parser = os.environ["GLIMMER_TOOL_PARSER"]

    comando = [
        "vllm", "serve", modelo,
        "--host", "0.0.0.0",
        "--port", str(PUERTO),
        "--api-key", os.environ["VLLM_API_KEY"],
        "--served-model-name", modelo,
        # El agente depende del tool calling: sin esto vLLM no devuelve tool_calls.
        "--enable-auto-tool-choice",
        "--tool-call-parser", parser,
        "--max-model-len", os.environ.get("GLIMMER_MAX_MODEL_LEN", "16384"),
        # No guardar prompts en logs: llevan datos de pacientes.
        "--disable-log-requests",
    ]
    if os.environ.get("GLIMMER_QUANTIZATION"):
        comando += ["--quantization", os.environ["GLIMMER_QUANTIZATION"]]

    subprocess.Popen(comando)
