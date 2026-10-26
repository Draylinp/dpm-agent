# Deploying to Azure

*Versión en español: [es/DESPLIEGUE.md](es/DESPLIEGUE.md)*

The app runs on **Azure App Service (Linux, .NET 8)** with **Azure Database for PostgreSQL – Flexible Server**. Secrets live in *App Settings* or **Key Vault**, never in the repository. GitHub Actions deploys with OIDC, so no Azure password is stored in GitHub.

## 1. Resources

```bash
GROUP=dpm-agent
REGION=eastus2
APP=dpm-agent-<unique-suffix>
PG=dpm-agent-pg-<unique-suffix>

az group create -n $GROUP -l $REGION

# Database
az postgres flexible-server create -g $GROUP -n $PG -l $REGION \
  --tier Burstable --sku-name Standard_B1ms --storage-size 32 \
  --admin-user dpmadmin --admin-password '<strong-password>' --public-access 0.0.0.0
az postgres flexible-server db create -g $GROUP -s $PG -d dpm

# App Service
az appservice plan create -g $GROUP -n $APP-plan --is-linux --sku B1
az webapp create -g $GROUP -p $APP-plan -n $APP --runtime "DOTNETCORE:8.0"
az webapp config set -g $GROUP -n $APP --always-on true --http20-enabled true --min-tls-version 1.2
az webapp update -g $GROUP -n $APP --https-only true
az webapp config set -g $GROUP -n $APP --generic-configurations '{"healthCheckPath": "/health/ready"}'
```

**Always On** is required: the waiting list, reminders, the WhatsApp processor and data retention run in the background.

## 2. Configuration (App Settings)

```bash
az webapp config appsettings set -g $GROUP -n $APP --settings \
  ASPNETCORE_ENVIRONMENT=Production \
  Logging__Formato=json \
  BaseDatos__Proveedor=Postgres \
  Limites__ConfiarEncabezadosReenviados=true \
  META_API_KEY='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/meta-api-key/)' \
  ConnectionStrings__Dpm='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/dpm-connection/)' \
  Panel__ClaveInicial='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/panel-initial-password/)' \
  WhatsApp__AccessToken='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/whatsapp-token/)' \
  WhatsApp__AppSecret='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/whatsapp-app-secret/)' \
  WhatsApp__VerifyToken='@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/whatsapp-verify-token/)' \
  WhatsApp__PhoneNumberId='<phone-number-id>'
```

The PostgreSQL connection string looks like `Host=<pg>.postgres.database.azure.com;Database=dpm;Username=dpmadmin;Password=<password>;Ssl Mode=Require`.

For App Service to read Key Vault, enable the app's managed identity (`az webapp identity assign`) and give it the **Key Vault Secrets User** role on the vault.

| Variable | Purpose |
|---|---|
| `Limites__ConfiarEncabezadosReenviados=true` | App Service puts a proxy in front: this lets rate limiting see the real client IP |
| `Limites__MensajesPorMinutoPorSesion` | 12 by default |
| `Limites__PeticionesPorMinutoPorIp` | 120 by default |
| `Retencion__DiasConversaciones` | 30 by default; the audit log keeps 365 (`Retencion__DiasBitacora`) |
| `DataProtection__Ruta` | Containers only: persistent folder for the keys that encrypt the dashboard cookie (App Service persists them on its own) |

On startup the app applies migrations and creates the `secretaria` user with `Panel__ClaveInicial`. After the first sign-in, create the real users from the dashboard and change or remove that variable.

## 3. GitHub Actions with OIDC

1. Create an app registration in Entra ID and a **federated credential** for the repository (`repo:Draylinp/dpm-agent:environment:produccion`).
2. Give it the **Website Contributor** role on the Web App.
3. In GitHub, create the `produccion` environment with the secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID`, and the repository variable `AZURE_WEBAPP_NAME`.

The workflow `.github/workflows/deploy-azure.yml` runs the tests, publishes and deploys on every push to `main` (or manually). Without the `AZURE_WEBAPP_NAME` variable it does nothing.

## 4. Container (alternative)

```bash
docker build -t dpm-agent .
docker run -p 8080:8080 \
  -e META_API_KEY=... \
  -e BaseDatos__Proveedor=Postgres -e ConnectionStrings__Dpm="Host=..." \
  -e Panel__ClaveInicial=... \
  -e DataProtection__Ruta=/data/keys -v dpm-data:/data \
  dpm-agent
```

With SQLite inside the container, use `-e ConnectionStrings__Dpm="Data Source=/data/dpm.db"` and the `/data` volume. For a clinic install with PostgreSQL in one command, see [INSTALLATION.md](INSTALLATION.md).

## 5. Single instance

Background jobs are designed for **one instance** (a B1 plan without horizontal scaling). With several instances, message dispatch and the scheduling engine would have to move to a single worker.
