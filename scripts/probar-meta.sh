#!/usr/bin/env bash
# Comprobación rápida de la Meta Model API con curl. Uso:
#   META_API_KEY=... ./scripts/probar-meta.sh [nota.ogg] [carnet.jpg]
set -euo pipefail

BASE="${Meta__BaseUrl:-https://api.meta.ai/v1}"
BASE="${BASE%/}"
MODELO="${Meta__Model:-muse-spark-1.2}"
: "${META_API_KEY:?Defina META_API_KEY}"
AUTH="Authorization: Bearer ${META_API_KEY}"

echo "== Modelos disponibles"
curl -sS "$BASE/models" -H "$AUTH" | head -c 2000; echo

echo "== Chat con tool calling ($MODELO)"
curl -sS "$BASE/chat/completions" -H "$AUTH" -H "Content-Type: application/json" -d @- <<JSON | head -c 3000; echo
{
  "model": "$MODELO",
  "max_completion_tokens": 200,
  "messages": [{"role": "user", "content": "¿Qué médicos hay en el consultorio?"}],
  "tools": [{"type": "function", "function": {"name": "listar_medicos", "description": "Lista los médicos.",
    "parameters": {"type": "object", "properties": {}, "required": [], "additionalProperties": false}}}],
  "tool_choice": "auto"
}
JSON

if [[ -n "${1:-}" ]]; then
  echo "== Transcripción de $1"
  curl -sS "$BASE/audio/transcriptions" -H "$AUTH" \
    -F "file=@$1" -F "model=${Transcripcion__Modelo:-muse-voice-transcribe-1.0}" -F "language=es" | head -c 2000; echo
fi

if [[ -n "${2:-}" ]]; then
  echo "== Lectura de carnet de $2"
  TIPO=$(file -b --mime-type "$2")
  IMAGEN=$(base64 -w0 "$2")
  curl -sS "$BASE/chat/completions" -H "$AUTH" -H "Content-Type: application/json" -d @- <<JSON | head -c 3000; echo
{
  "model": "$MODELO",
  "max_completion_tokens": 300,
  "messages": [{"role": "user", "content": [
    {"type": "text", "text": "Lee el nombre, la ARS y el número de afiliado de este carnet."},
    {"type": "image_url", "image_url": {"url": "data:$TIPO;base64,$IMAGEN"}}]}],
  "response_format": {"type": "json_schema", "json_schema": {"name": "carnet_ars", "strict": true, "schema": {
    "type": "object", "additionalProperties": false,
    "required": ["es_carnet", "nombre", "ars", "numero_afiliado"],
    "properties": {"es_carnet": {"type": "boolean"}, "nombre": {"type": ["string", "null"]},
      "ars": {"type": ["string", "null"]}, "numero_afiliado": {"type": ["string", "null"]}}}}}
}
JSON
fi
