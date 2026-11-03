# Installation with Docker Compose

Spanish version: [es/INSTALACION.md](es/INSTALACION.md).

One command brings up the app and PostgreSQL on a clinic computer or a small server. An optional profile adds Muse Glimmer on a local GPU, so conversations never leave the clinic.

## Requirements

- Docker with the Compose plugin (`docker compose version`).
- A Meta Model API key (Muse Spark, Muse Voice Transcribe, insurance-card reading).
- For the `glimmer` profile only: an NVIDIA GPU with 24 GB or more and the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html).

## 1. Settings

```bash
git clone https://github.com/Draylinp/dpm-agent.git
cd dpm-agent
cp .env.example .env
```

Fill in at least these three values in `.env`:

| Variable | What it is |
|---|---|
| `META_API_KEY` | Meta Model API key |
| `POSTGRES_PASSWORD` | Database password: long and random (`openssl rand -base64 24`) |
| `Panel__ClaveInicial` | Password of the initial `secretaria` user (admin), at least 12 characters and not a common one; the dashboard asks to change it at the first sign-in |

`.env` is in `.gitignore`: never commit it. Every other line in `.env.example` has a working default and a comment. Any setting from `appsettings.json` can be added as `Section__Key=value`.

For a real clinic set `BaseDatos__SembrarDatosDemo=false` before the first start, so the fictional sample clinic is not created.

## 2. Start

```bash
docker compose up -d --build
docker compose logs -f app      # until "Now listening on: http://[::]:8080"
```

Open `http://localhost:8080` (chat) and `http://localhost:8080/panel.html` (dashboard, user `secretaria`). On first start the app applies the database migrations by itself.

| Check | Command |
|---|---|
| Liveness | `curl http://localhost:8080/health/live` |
| Database and model | `curl http://localhost:8080/health/ready` (`Degraded` with a missing key) |

## 3. What runs

| Service | Image | Data |
|---|---|---|
| `app` | built from the `Dockerfile` (ASP.NET Core 8, non-root user) | `claves` volume: keys that encrypt dashboard cookies |
| `db` | `postgres:16`, no published port | `datos` volume |
| `glimmer` (profile) | `vllm/vllm-openai` | `modelos` volume: Hugging Face cache |

Back up the `datos` volume (`docker compose exec db pg_dump -U dpm dpm > backup.sql`). Updating: `git pull && docker compose up -d --build`.

## 4. Optional: Muse Glimmer on the clinic's GPU

The `glimmer` profile serves the open-weights model with vLLM and an OpenAI-compatible API. In `.env`:

```bash
GLIMMER_MODELO=<Hugging Face repo of the model>
GLIMMER_TOOL_PARSER=<vLLM tool-call parser for that model>
HF_TOKEN=<only if the repo requires it>
OpenAICompatible__ApiKey=<a long random key shared by the app and vLLM>
OpenAICompatible__BaseUrl=http://glimmer:8000/v1/
OpenAICompatible__Model=<same value as GLIMMER_MODELO>

# Glimmer as the main model...
Modelo__Proveedor=OpenAICompatible
# ...or Muse Spark as main and Glimmer as fallback:
# Modelo__Proveedor=MetaApi
# Modelo__Respaldo=OpenAICompatible
```

```bash
docker compose --profile glimmer up -d --build
docker compose logs -f glimmer   # the first start downloads the weights
```

How to pick the repo and the parser: [GLIMMER.md](GLIMMER.md). Voice notes and card photos still go to the Meta Model API.

## 5. Access from other computers (HTTPS)

Outside development the dashboard cookie is `Secure`: browsers only keep it over HTTPS (or on `localhost`). To use the dashboard from the reception computer, put an HTTPS proxy in front. A minimal example with Caddy in `docker-compose.override.yml`:

```yaml
services:
  caddy:
    image: caddy:2
    ports: ["443:443"]
    command: caddy reverse-proxy --from https://dpm.local --to app:8080 --internal-certs
    volumes: [caddy:/data]
  app:
    environment:
      Limites__ConfiarEncabezadosReenviados: "true"
volumes:
  caddy:
```

Then trust Caddy's local certificate on each computer, or use a real domain (Caddy gets the certificate by itself).

## 6. Prices for the cost view

Model prices go per model name, which contains `-` and `.`. In `docker-compose.override.yml`:

```yaml
services:
  app:
    environment:
      "Precios__Modelos__muse-spark-1.2__EntradaPorMillon": "<price>"
      "Precios__Modelos__muse-spark-1.2__SalidaPorMillon": "<price>"
```

See [COSTS.md](COSTS.md).

## 7. WhatsApp

Fill the `WhatsApp__*` values in `.env` and point the Meta webhook to `https://<your-domain>/api/whatsapp/webhook`. The webhook needs a public HTTPS address. See [WHATSAPP.md](WHATSAPP.md).

## Troubleshooting

| Symptom | Cause |
|---|---|
| `required variable POSTGRES_PASSWORD is missing` | `.env` is missing or the value is empty |
| No dashboard user; the logs say "Defina Panel__ClaveInicial" | Missing initial password |
| The app stops at startup with an error about the password | Initial password shorter than 10 characters |
| Login works on `localhost` but not from another computer | HTTPS is needed (section 5) |
| `glimmer` exits right away | Empty `GLIMMER_MODELO` / `GLIMMER_TOOL_PARSER`, no GPU visible, or the repo needs `HF_TOKEN` |
