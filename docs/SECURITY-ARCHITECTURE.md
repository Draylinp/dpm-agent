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

## Dashboard access

- **Passwords**: PBKDF2 hashes (ASP.NET Core `PasswordHasher`); at least 12 characters, not a common password or a variation of one (Spanish and clinic-specific words included), no sequences and not containing the username (`Panel/PoliticaClaves.cs`). A password set by someone else (the initial one, or a user created by an admin) must be changed at the first sign-in: until then the session can only change it. A stored password that no longer meets the policy is flagged for change at the next sign-in. The public demo user is the only exception: its password is shown on the demo page and cannot be changed.
- **Sessions**: `HttpOnly`, `Secure`, `SameSite=Strict` cookie that expires after 30 minutes without activity (`Panel__MinutosInactividad`) and always after 8 hours (`Panel__HorasMaximasSesion`). Each session carries a security stamp; changing the password or the second factor changes it, which signs out every other session of that user on its next request. A deactivated user is signed out the same way.
- **Sign-in**: the same answer for an unknown user, a wrong password, a locked or inactive account; a password hash is always computed so the timing does not reveal which users exist. Per-user lockout (5 failures, 15 minutes) plus a per-IP rate limit on sign-in, password change and second-factor endpoints.
- **Second factor (TOTP)**: optional for administrators, required for all of them if the clinic turns it on (dashboard → Data access log). Standard RFC 6238 codes (any authenticator app); the secret is stored encrypted with the Data Protection API, a code cannot be reused and a wrong code counts as a failed attempt.
- **Data access log**: who viewed a patient's record, exported or deleted their data: only the staff username, the action, the patient id and the time, never personal data. Only administrators see it (dashboard → Data access log).

Tests: `tests/DpmAgent.Tests/SesionesPanelTests.cs`.
