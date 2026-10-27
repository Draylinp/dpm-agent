# Despliegue en Azure

*English version: [../DEPLOYMENT.md](../DEPLOYMENT.md)*

La app corre en **Azure App Service (Linux, .NET 8)** con **Azure Database for PostgreSQL – Flexible Server**. Los secretos van en *App Settings* o en **Key Vault**, nunca en el repositorio. GitHub Actions despliega con OIDC, así que no se guarda ninguna contraseña de Azure en GitHub.

## 1. Recursos

```bash
GRUPO=dpm-agent
REGION=eastus2
APP=dpm-agent-<sufijo-unico>
PG=dpm-agent-pg-<sufijo-unico>

az group create -n $GRUPO -l $REGION

# Base de datos
az postgres flexible-server create -g $GRUPO -n $PG -l $REGION \
  --tier Burstable --sku-name Standard_B1ms --storage-size 32 \
  --admin-user dpmadmin --admin-password '<clave-fuerte>' --public-access 0.0.0.0
az postgres flexible-server db create -g $GRUPO -s $PG -d dpm

# App Service
az appservice plan create -g $GRUPO -n $APP-plan --is-linux --sku B1
az webapp create -g $GRUPO -p $APP-plan -n $APP --runtime "DOTNETCORE:8.0"
az webapp config set -g $GRUPO -n $APP --always-on true --http20-enabled true --min-tls-version 1.2
az webapp update -g $GRUPO -n $APP --https-only true
az webapp config set -g $GRUPO -n $APP --generic-configurations '{"healthCheckPath": "/health/ready"}'
```

**Always On** es necesario: la lista de espera, los recordatorios, el procesador de WhatsApp y la retención corren en segundo plano.

## 2. Configuración (App Settings)

```bash
az webapp config appsettings set -g $GRUPO -n $APP --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  Logging__Formato=json \
  BaseDatos__Proveedor=Postgres \
  Limites__ConfiarEncabezadosReenviados=true \
  META_API_KEY='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/meta-api-key/)' \
  ConnectionStrings__Dpm='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/conexion-dpm/)' \
  Panel__ClaveInicial='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/panel-clave-inicial/)' \
  WhatsApp__AccessToken='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/whatsapp-token/)' \
  WhatsApp__AppSecret='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/whatsapp-app-secret/)' \
  WhatsApp__VerifyToken='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/whatsapp-verify-token/)' \
  WhatsApp__PhoneNumberId='<phone-number-id>'
```

La cadena de PostgreSQL tiene la forma `Host=<pg>.postgres.database.azure.com;Database=dpm;Username=dpmadmin;Password=<clave>;Ssl Mode=Require`. `Ssl Mode=Require` (o `VerifyFull`) es obligatorio: en producción la app no arranca sin TLS hacia la base. En App Service la plataforma guarda las llaves de Data Protection (que también cifran el teléfono y el afiliado de los pacientes); en un contenedor, `DataProtection__Ruta` debe apuntar a un volumen o la app no arranca.

Para que App Service lea Key Vault: activa la identidad administrada de la app (`az webapp identity assign`) y dale el rol **Key Vault Secrets User** sobre el vault.

| Variable | Para qué |
|---|---|
| `Limites__ConfiarEncabezadosReenviados=true` | App Service pone un proxy delante: así el rate limiting ve la IP real del cliente |
| `Limites__MensajesPorMinutoPorSesion` | 12 por defecto |
| `Limites__PeticionesPorMinutoPorIp` | 120 por defecto |
| `Retencion__DiasConversaciones` | 30 por defecto; la bitácora, 365 (`Retencion__DiasBitacora`) |
| `DataProtection__Ruta` | Solo en contenedores: carpeta persistente para las claves que cifran la cookie del panel (App Service ya las persiste solo) |

Al arrancar, la app aplica las migraciones y crea el usuario `secretaria` con `Panel__ClaveInicial`. El primer inicio de sesión obliga a poner una clave nueva (la inicial solo sirve una vez); después crea los usuarios reales desde el panel y retira esa variable.

### Secretos: referencias a Key Vault y rotación

Todos los secretos van en Key Vault y los App Settings solo guardan una **referencia** (`@Microsoft.KeyVault(SecretUri=...)`), nunca el valor. Use un SecretUri **sin versión** para que App Service tome la última: refresca las referencias periódicamente (cada 24 horas aproximadamente) y en cada reinicio o cambio de configuración.

**Rotar la clave de Meta** (`META_API_KEY`):
1. Cree una clave nueva en la consola de desarrolladores de Meta (deje la anterior activa).
2. `az keyvault secret set --vault-name <vault> --name meta-api-key --value '<clave nueva>'` (una versión nueva del mismo secreto).
3. `az webapp restart -g $GROUP -n $APP` para que la app la lea ya; revise `/health/ready` y envíe un mensaje de prueba en la demo.
4. Revoque la clave anterior en la consola de Meta. Si una clave pudo filtrarse, haga los pasos 1 a 4 de inmediato y revise la vista de costos por usos extraños.

**Rotar el app secret de WhatsApp** (`WhatsApp__AppSecret`, con el que se verifica la firma del webhook):
1. En el panel de la app de Meta (Configuración de la app → Básica) use *Restablecer* en la clave secreta: desde ese momento Meta firma los webhooks con la nueva.
2. Enseguida, `az keyvault secret set --vault-name <vault> --name whatsapp-app-secret --value '<secreto nuevo>'` y `az webapp restart`. Los webhooks que lleguen entre medio fallan la firma y se rechazan (401); Meta los reintenta, así que no se pierde nada si el reinicio ocurre en pocos minutos.
3. Rote igual el token de acceso (`WhatsApp__AccessToken`) si usa un token de usuario del sistema de larga duración; `WhatsApp__VerifyToken` puede ser cualquier valor aleatorio nuevo, y luego se actualiza en la configuración del webhook.

Las llaves de Data Protection no son un secreto que se rote a mano: ASP.NET Core las rota cada 90 días y conserva las anteriores para leer los datos existentes. Nunca borre esa carpeta (o, en App Service, `/home/ASP.NET/DataProtection-Keys`): los campos cifrados de los pacientes quedarían ilegibles.

## 3. GitHub Actions con OIDC

1. Crea un registro de aplicación en Entra ID y una **credencial federada** para el repositorio (`repo:Draylinp/dpm-agent:environment:produccion`).
2. Dale el rol **Website Contributor** sobre la Web App.
3. En GitHub, crea el entorno `produccion` con los secretos `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` y `AZURE_SUBSCRIPTION_ID`, y la variable del repositorio `AZURE_WEBAPP_NAME`.

El workflow `.github/workflows/deploy-azure.yml` corre las pruebas, publica y despliega en cada push a `main` (o a mano). Sin la variable `AZURE_WEBAPP_NAME` no hace nada.

## 4. Contenedor (alternativa)

```bash
docker build -t dpm-agent .
docker run -p 8080:8080 \
  -e META_API_KEY=... \
  -e BaseDatos__Proveedor=Postgres -e ConnectionStrings__Dpm="Host=..." \
  -e Panel__ClaveInicial=... \
  -e DataProtection__Ruta=/data/keys -v dpm-data:/data \
  dpm-agent
```

Con SQLite dentro del contenedor, usa `-e ConnectionStrings__Dpm="Data Source=/data/dpm.db"` y el volumen `/data`. Para instalar en un consultorio con PostgreSQL en un solo comando, ver [INSTALACION.md](INSTALACION.md).

## 5. Una sola instancia

Las tareas en segundo plano están pensadas para **una instancia** (plan B1 sin escalado horizontal). Con varias instancias habría que mover el despacho de mensajes y el motor de agenda a un único trabajador.
