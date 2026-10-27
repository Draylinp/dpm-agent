# Instalación con Docker Compose

Versión en inglés: [../INSTALLATION.md](../INSTALLATION.md).

Un solo comando levanta la app y PostgreSQL en una computadora del consultorio o un servidor pequeño. Un perfil opcional agrega Muse Glimmer en una GPU local, para que las conversaciones no salgan del consultorio.

## Requisitos

- Docker con el plugin de Compose (`docker compose version`).
- Una clave de la Meta Model API (Muse Spark, Muse Voice Transcribe, lectura del carnet).
- Solo para el perfil `glimmer`: una GPU NVIDIA de 24 GB o más y el [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html).

## 1. Ajustes

```bash
git clone https://github.com/Draylinp/dpm-agent.git
cd dpm-agent
cp .env.example .env
```

Complete al menos estos tres valores en `.env`:

| Variable | Qué es |
|---|---|
| `META_API_KEY` | Clave de la Meta Model API |
| `POSTGRES_PASSWORD` | Clave de la base de datos: larga y aleatoria (`openssl rand -base64 24`) |
| `Panel__ClaveInicial` | Clave del usuario inicial `secretaria` (admin), de 12 caracteres o más y no común; el panel pide cambiarla en el primer inicio de sesión |

`.env` está en `.gitignore`: nunca lo suba al repositorio. Las demás líneas de `.env.example` traen un valor que funciona y un comentario. Cualquier ajuste de `appsettings.json` se agrega como `Seccion__Clave=valor`.

En un consultorio real ponga `BaseDatos__SembrarDatosDemo=false` antes del primer arranque, para que no se cree el consultorio ficticio de ejemplo.

## 2. Arrancar

```bash
docker compose up -d --build
docker compose logs -f app      # hasta ver "Now listening on: http://[::]:8080"
```

Abra `http://localhost:8080` (chat) y `http://localhost:8080/panel.html` (panel, usuario `secretaria`). En el primer arranque la app aplica sola las migraciones de la base.

| Verificación | Comando |
|---|---|
| La app responde | `curl http://localhost:8080/health/live` |
| Base de datos y modelo | `curl http://localhost:8080/health/ready` (`Degraded` si falta la clave) |

## 3. Qué corre

| Servicio | Imagen | Datos |
|---|---|---|
| `app` | se construye con el `Dockerfile` (ASP.NET Core 8, usuario sin privilegios) | volumen `claves`: claves que cifran las cookies del panel |
| `db` | `postgres:16`, sin puerto publicado | volumen `datos` |
| `glimmer` (perfil) | `vllm/vllm-openai` | volumen `modelos`: caché de Hugging Face |

Respalde el volumen `datos` (`docker compose exec db pg_dump -U dpm dpm > respaldo.sql`). Para actualizar: `git pull && docker compose up -d --build`.

## 4. Opcional: Muse Glimmer en la GPU del consultorio

El perfil `glimmer` sirve el modelo de pesos abiertos con vLLM y una API compatible con OpenAI. En `.env`:

```bash
GLIMMER_MODELO=<repo de Hugging Face del modelo>
GLIMMER_TOOL_PARSER=<parser de herramientas de vLLM para ese modelo>
HF_TOKEN=<solo si el repo lo pide>
OpenAICompatible__ApiKey=<una clave larga y aleatoria que comparten la app y vLLM>
OpenAICompatible__BaseUrl=http://glimmer:8000/v1/
OpenAICompatible__Model=<el mismo valor de GLIMMER_MODELO>

# Glimmer como modelo principal...
Modelo__Proveedor=OpenAICompatible
# ...o Muse Spark como principal y Glimmer como respaldo:
# Modelo__Proveedor=MetaApi
# Modelo__Respaldo=OpenAICompatible
```

```bash
docker compose --profile glimmer up -d --build
docker compose logs -f glimmer   # el primer arranque descarga los pesos
```

Cómo elegir el repo y el parser: [GLIMMER.md](GLIMMER.md). Las notas de voz y las fotos del carnet siguen yendo a la Meta Model API.

## 5. Acceso desde otras computadoras (HTTPS)

Fuera de desarrollo la cookie del panel es `Secure`: los navegadores solo la guardan con HTTPS (o en `localhost`). Para usar el panel desde la computadora de la recepción, ponga un proxy HTTPS delante. Un ejemplo mínimo con Caddy en `docker-compose.override.yml`:

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

Luego confíe en el certificado local de Caddy en cada computadora, o use un dominio real (Caddy obtiene el certificado solo).

## 6. Precios para la vista de costos

Los precios van por nombre de modelo, que lleva `-` y `.`. En `docker-compose.override.yml`:

```yaml
services:
  app:
    environment:
      "Precios__Modelos__muse-spark-1.2__EntradaPorMillon": "<precio>"
      "Precios__Modelos__muse-spark-1.2__SalidaPorMillon": "<precio>"
```

Ver [COSTOS.md](COSTOS.md).

## 7. WhatsApp

Complete los valores `WhatsApp__*` en `.env` y apunte el webhook de Meta a `https://<su-dominio>/api/whatsapp/webhook`. El webhook necesita una dirección HTTPS pública. Ver [WHATSAPP.md](WHATSAPP.md).

## Problemas comunes

| Síntoma | Causa |
|---|---|
| `required variable POSTGRES_PASSWORD is missing` | Falta `.env` o el valor está vacío |
| No hay usuario del panel; los logs dicen "Defina Panel__ClaveInicial" | Falta la clave inicial |
| La app se detiene al arrancar con un error sobre la clave | La clave inicial tiene menos de 10 caracteres |
| El login funciona en `localhost` pero no desde otra computadora | Hace falta HTTPS (sección 5) |
| `glimmer` se cierra enseguida | `GLIMMER_MODELO` / `GLIMMER_TOOL_PARSER` vacíos, sin GPU visible, o el repo pide `HF_TOKEN` |
