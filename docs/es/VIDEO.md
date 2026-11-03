# Grabar el video de 3 minutos

*English version: [../VIDEO.md](../VIDEO.md)*

## 1. Iniciar la demo (Windows)

1. Instale el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) y [Git](https://git-scm.com/download/win).
2. Abra PowerShell y ejecute:
   ```powershell
   cd $HOME
   git clone https://github.com/Draylinp/dpm-agent.git
   cd dpm-agent
   powershell -ExecutionPolicy Bypass -File scripts\iniciar-demo.ps1
   ```
3. Pegue su clave de Meta cuando la pida (no se muestra ni se guarda). Se abren dos pestañas: la demo (`http://localhost:5199/`) y el panel (`/panel.html`, usuario `demo`, clave `demo-video-2026`).
4. Ponga la interfaz en **inglés** (selector arriba a la derecha) en las dos pestañas: la competencia es en inglés. Pulse **Reset demo** antes de cada toma.

Grabe con **Win + G** (Xbox Game Bar) u [OBS Studio](https://obsproject.com/). Súbalo a YouTube como *No listado*.

## 2. Guion

| Tiempo | En pantalla | Qué decir (en inglés) |
|---|---|---|
| 0:00–0:25 | Título y la página de la demo | El problema: el WhatsApp del consultorio, la secretaria saturada, huecos vacíos, pacientes en kreyòl. |
| 0:25–0:55 | **Book in English** y **Book in Haitian Creole**; señale el panel *Agent steps* | Identifica al paciente por su número y llama herramientas paso a paso con Muse Spark. |
| 0:55–1:20 | **Clinical question** y **Emergency** | Nunca da consejo médico: lo clínico va a una persona y la emergencia al 911; un filtro revisa cada respuesta. |
| 1:20–1:45 | **See me today** y **The doctor orders a follow-up** (responda "yes") | No juzga la gravedad; el sobrecupo solo lo aprueba el personal; el médico tiene su canal verificado. |
| 1:45–2:15 | Panel: *Escalations*, *Urgent requests*, *Follow-ups*, *Families* | Lo que necesita a una persona, seguimientos, urgencias, familias y reprogramaciones. |
| 2:15–2:40 | README, sección *How it uses Meta AI* | Muse Spark, Muse Voice Transcribe, Muse Glimmer y Llama Guard con pesos abiertos, WhatsApp Cloud API. |
| 2:40–3:00 | Ejecución verde de GitHub Actions o la sección *Security* | Datos cifrados, CSP estricta, 2FA, 235 casos en el CI. Siguiente paso: un consultorio piloto. |

Consejos: use solo los datos ficticios de la demo, tomas cortas, y diga con claridad si alguna parte usó un respaldo en vez de la API de Meta.
