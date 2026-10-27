# Arquitectura de seguridad

*English version: [../SECURITY-ARCHITECTURE.md](../SECURITY-ARCHITECTURE.md)*

Cómo DPM Agent protege las páginas del navegador, la API y los datos. Las rutas son relativas a la raíz del repositorio.

## Protección del navegador

Cada respuesta (páginas y API) lleva estas cabeceras, definidas en `src/DpmAgent.Api/Seguridad/EncabezadosSeguridad.cs`:

| Cabecera | Valor | Por qué |
|---|---|---|
| `Content-Security-Policy` | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'` | Sin `unsafe-inline`: cada script y estilo está en su propio archivo en `wwwroot/`, así que un HTML inyectado no puede ejecutar código. Nadie puede incrustar las páginas, cargar complementos, cambiar la URL base ni enviar formularios a otro sitio. |
| `X-Frame-Options` | `DENY` | Protección contra clickjacking en navegadores viejos. |
| `Permissions-Policy` | `microphone=(self)` solo en la página del chat (`/`), `microphone=()` en el resto; cámara, ubicación, pagos, USB y sensores siempre apagados | El chat graba notas de voz; el panel nunca necesita el micrófono. |
| `Cross-Origin-Opener-Policy` | `same-origin` | Otro sitio abierto en una ventana emergente no conserva acceso a estas páginas. |
| `X-Content-Type-Options`, `Referrer-Policy` | `nosniff`, `no-referrer` | Sin adivinar tipos MIME; las URL no se filtran a otros sitios. |
| `Strict-Transport-Security` | fuera de desarrollo | Solo HTTPS. |

El CI lo comprueba dos veces: `tests/DpmAgent.Tests/SeguridadNavegadorTests.cs` verifica las cabeceras y que las páginas no tengan scripts, estilos, atributos de eventos ni URL `javascript:` en línea; `tests/accesibilidad/auditar.mjs` abre la demo (y usa un ejemplo), las pestañas del panel y el aviso de privacidad en Chromium y falla ante cualquier violación de la CSP o error de JavaScript en la consola.

## Peticiones entre sitios

- **CSRF.** La cookie del panel es `HttpOnly`, `Secure` y `SameSite=Strict`. Además, toda petición que cambia datos en `/api/panel`, `/api/bitacora`, `/api/carnets` y `/api/auth` (POST, PUT, PATCH, DELETE) debe traer una cabecera `Origin` (o, si el navegador no la manda, un `Referer`) con el mismo esquema, host y puerto; si no, recibe `403`. Una petición sin ninguna de las dos también se rechaza. Ver `ProteccionCsrf` en `Seguridad/EncabezadosSeguridad.cs`.
- **Sin CORS.** La API no habilita CORS: nunca envía `Access-Control-Allow-Origin`, así que una página de otro origen no puede leer sus respuestas, y las peticiones previas (preflight) de otros orígenes no reciben permiso. El chat, la demo y el panel se sirven desde el mismo origen que la API, así que no lo necesitan. Si un consultorio necesitara otra interfaz, debe servirse desde el mismo dominio (proxy inverso) en vez de abrir CORS. Probado en `SeguridadNavegadorTests`.
- **Webhook.** `/api/whatsapp/webhook` no usa cookie y no pasa por la revisión de origen: cada llamada debe traer la firma HMAC `X-Hub-Signature-256` de Meta (ver [WHATSAPP.md](WHATSAPP.md)).
