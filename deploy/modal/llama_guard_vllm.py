"""
Sirve Llama Guard (pesos abiertos de Meta) con vLLM en Modal, para el filtro de salida.

    modal secret create dpm-llama-guard \
        VLLM_API_KEY=<clave-larga-aleatoria> \
        GUARD_MODEL_ID=<repo exacto de Hugging Face, por ejemplo la versión de Llama Guard que tenga acceso> \
        HF_TOKEN=<token de Hugging Face con acceso al repo>
    modal deploy deploy/modal/llama_guard_vllm.py

En la app: FiltroSalida__Proveedor=LlamaGuard, FiltroSalida__BaseUrl=<url>/v1/,
FiltroSalida__Model=<GUARD_MODEL_ID>, FiltroSalida__ApiKey=<VLLM_API_KEY>.
"""

import os
import subprocess

import modal

GPU = os.environ.get("GUARD_GPU", "L4")
VLLM_VERSION = os.environ.get("VLLM_VERSION", "")
PUERTO = 8000

imagen = (
    modal.Image.debian_slim(python_version="3.12")
    .pip_install(f"vllm=={VLLM_VERSION}" if VLLM_VERSION else "vllm", "huggingface_hub[hf_transfer]")
    .env({"HF_HUB_ENABLE_HF_TRANSFER": "1"})
)
cache_hf = modal.Volume.from_name("dpm-guard-hf-cache", create_if_missing=True)

app = modal.App("dpm-llama-guard")


@app.function(
    image=imagen,
    gpu=GPU,
    secrets=[modal.Secret.from_name("dpm-llama-guard")],
    volumes={"/root/.cache/huggingface": cache_hf},
    scaledown_window=15 * 60,
    timeout=10 * 60,
)
@modal.concurrent(max_inputs=32)
@modal.web_server(port=PUERTO, startup_timeout=20 * 60)
def serve():
    modelo = os.environ["GUARD_MODEL_ID"]
    subprocess.Popen([
        "vllm", "serve", modelo,
        "--host", "0.0.0.0",
        "--port", str(PUERTO),
        "--api-key", os.environ["VLLM_API_KEY"],
        "--served-model-name", modelo,
        "--max-model-len", "8192",
        # Los turnos que clasifica llevan datos de pacientes: no se guardan en logs.
        "--disable-log-requests",
    ])
