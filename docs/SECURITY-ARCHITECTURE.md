# Security architecture

*Versión en español: [es/ARQUITECTURA-SEGURIDAD.md](es/ARQUITECTURA-SEGURIDAD.md)*

How DPM Agent protects the browser pages, the API and the data. Paths are relative to the repository root.

## Browser protections

Every response (pages and API) carries these headers, set in `src/DpmAgent.Api/Seguridad/EncabezadosSeguridad.cs`:

| Header | Value | Why |
|---|---|---|
| `Content-Security-Policy` | `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; media-src 'self' blob:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'` | No `unsafe-inline`: every script and style lives in its own file under `wwwroot/`, so injected markup cannot run code. Nothing can embed the pages, load plugins, change the base URL or post forms elsewhere. |
| `X-Frame-Options` | `DENY` | Clickjacking protection for older browsers. |
| `Permissions-Policy` | `microphone=(self)` only on the chat page (`/`), `microphone=()` everywhere else; camera, geolocation, payment, USB and motion sensors always off | The chat records voice notes; the dashboard never needs the microphone. |
| `Cross-Origin-Opener-Policy` | `same-origin` | Another site opened in a popup cannot keep a handle on these pages. |
| `X-Content-Type-Options`, `Referrer-Policy` | `nosniff`, `no-referrer` | No MIME sniffing; no URL leaks to other sites. |
| `Strict-Transport-Security` | outside development | HTTPS only. |

CI checks it twice: `tests/DpmAgent.Tests/SeguridadNavegadorTests.cs` asserts the headers and that the pages have no inline scripts, styles, event attributes or `javascript:` URLs; `tests/accesibilidad/auditar.mjs` opens the demo (and uses an example), the dashboard tabs and the privacy notice in Chromium and fails on any CSP violation or JavaScript error in the console.

## Cross-site requests

- **CSRF.** The dashboard cookie is `HttpOnly`, `Secure` and `SameSite=Strict`. On top of that, every request that changes data under `/api/panel`, `/api/bitacora`, `/api/carnets` and `/api/auth` (POST, PUT, PATCH, DELETE) must carry an `Origin` header (or, if the browser omits it, a `Referer`) from the same scheme, host and port; otherwise it gets `403`. A request with neither is rejected too. See `ProteccionCsrf` in `Seguridad/EncabezadosSeguridad.cs`.
- **No CORS.** The API does not enable CORS: it never sends `Access-Control-Allow-Origin`, so a page on another origin cannot read its responses, and preflight requests from other origins get no permission. The chat, the demo and the dashboard are served from the same origin as the API, so they do not need it. If a clinic ever needs a separate front end, it should be served from the same domain (reverse proxy) rather than opening CORS. Tested in `SeguridadNavegadorTests`.
- **Webhook.** `/api/whatsapp/webhook` has no cookie and is not covered by the origin check: every call must carry Meta's `X-Hub-Signature-256` HMAC (see [WHATSAPP.md](WHATSAPP.md)).
