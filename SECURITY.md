# Security policy

*Versión en español más abajo.*

## Reporting a vulnerability

Please **do not open a public issue** for a security problem. Report it privately through GitHub: on this repository go to **Security → Report a vulnerability** (GitHub private vulnerability reporting). Include what is affected, how to reproduce it and the impact you expect. If you cannot use GitHub, open an issue that only asks for a private contact channel, without technical details, and the maintainer will reply.

Please do not access, modify or delete data that is not yours, and do not run load or denial-of-service tests against the public demo. The demo only holds fictional data; testing against a copy you run yourself (`docker compose up`) is welcome.

## Supported versions

| Version | Supported |
|---|---|
| `main` branch and the latest deployment | Yes |
| Older commits and forks | No: update to the latest `main` |

## Response times (indicative)

This is a project maintained by one person, so these are goals, not guarantees:

| Step | Target |
|---|---|
| Acknowledge the report | 3 business days |
| First assessment (confirmed or not, severity) | 10 business days |
| Fix for a critical or high issue | 30 days |
| Fix for a medium or low issue | Next planned release |

You will be told when the fix is published and, if you wish, credited in the release notes.

## How the project is protected

See [docs/SECURITY-ARCHITECTURE.md](docs/SECURITY-ARCHITECTURE.md): browser headers and CSP, CSRF and CORS, dashboard sessions and second factor, field encryption, TLS, data flows to external services, secret scanning, CodeQL, Dependabot and image scanning.

---

# Política de seguridad

## Cómo reportar una vulnerabilidad

Por favor **no abra un issue público** para un problema de seguridad. Repórtelo en privado por GitHub: en este repositorio vaya a **Security → Report a vulnerability** (avisos privados de vulnerabilidades de GitHub). Incluya qué está afectado, cómo reproducirlo y el impacto que espera. Si no puede usar GitHub, abra un issue que solo pida un canal de contacto privado, sin detalles técnicos, y el mantenedor responderá.

No acceda, modifique ni borre datos que no sean suyos, y no haga pruebas de carga ni de denegación de servicio contra la demo pública. La demo solo tiene datos ficticios; probar contra una copia propia (`docker compose up`) es bienvenido.

## Versiones con soporte

| Versión | Soporte |
|---|---|
| Rama `main` y el último despliegue | Sí |
| Commits anteriores y forks | No: actualice al último `main` |

## Tiempos de respuesta (orientativos)

Es un proyecto que mantiene una sola persona, así que son metas, no garantías:

| Paso | Meta |
|---|---|
| Acusar recibo | 3 días hábiles |
| Primera evaluación (confirmado o no, gravedad) | 10 días hábiles |
| Corrección de un problema crítico o alto | 30 días |
| Corrección de un problema medio o bajo | Próxima versión planificada |

Se le avisará cuando se publique la corrección y, si lo desea, se le dará crédito en las notas de la versión.

## Cómo se protege el proyecto

Ver [docs/es/ARQUITECTURA-SEGURIDAD.md](docs/es/ARQUITECTURA-SEGURIDAD.md): cabeceras y CSP del navegador, CSRF y CORS, sesiones del panel y segundo factor, cifrado de campos, TLS, flujos de datos hacia servicios externos, escaneo de secretos, CodeQL, Dependabot y escaneo de la imagen.
