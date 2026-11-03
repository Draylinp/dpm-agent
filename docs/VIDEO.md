# Recording the 3-minute video

*Versión en español: [es/VIDEO.md](es/VIDEO.md)*

## 1. Start the demo (Windows)

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Git](https://git-scm.com/download/win).
2. Open PowerShell and run:
   ```powershell
   cd $HOME
   git clone https://github.com/Draylinp/dpm-agent.git
   cd dpm-agent
   powershell -ExecutionPolicy Bypass -File scripts\iniciar-demo.ps1
   ```
3. Paste your Meta API key when asked (it is not shown or saved). Two browser tabs open: the demo (`http://localhost:5199/`) and the dashboard (`/panel.html`, user `demo`, password `demo-video-2026`).
4. Set the interface language to **English** (top-right selector) in both tabs. Click **Reset demo** before each take.

Record with **Win + G** (Xbox Game Bar) or [OBS Studio](https://obsproject.com/). Upload to YouTube as *Unlisted*.

## 2. Shot list

| Time | On screen | What to say |
|---|---|---|
| 0:00–0:25 | Title, then the demo page | Clinics in the Dominican Republic run their schedule on WhatsApp; the receptionist answers the same questions all day, cancelled slots stay empty, and many patients write in Haitian Creole. |
| 0:25–0:55 | **Book in English**, then **Book in Haitian Creole**; point at the *Agent steps* panel | The agent identifies the patient by phone number and calls tools step by step with Muse Spark: check availability, confirm, book. Same in Kreyòl. |
| 0:55–1:20 | **Clinical question**, then **Emergency** | It never gives medical advice: the question goes to a person, and an emergency gets 911 and an urgent escalation. An output filter checks every reply. |
| 1:20–1:45 | **See me today**, then **The doctor orders a follow-up** (answer "yes") | It never judges severity: it offers the first slot or an urgent slot, or files a request only staff can approve. The doctor has his own verified channel. |
| 1:45–2:15 | Dashboard: *Escalations*, *Urgent requests*, *Follow-ups*, *Families* | The receptionist sees what needs a person; follow-ups, urgent slots, families sharing a phone, rescheduling when the doctor blocks a day. |
| 2:15–2:40 | README section *How it uses Meta AI* | Muse Spark for conversation, tool calling and reading insurance cards; Muse Voice Transcribe for voice notes; Muse Glimmer and Llama Guard with open weights so a clinic can run everything on its own hardware; WhatsApp Cloud API. |
| 2:40–3:00 | GitHub Actions run (green) or README *Security* | Encrypted patient data, strict CSP, 2FA for admins, and 235 evaluation cases in CI. Next step: a pilot clinic. |

Tips: use only the fictional data of the demo, keep each take short, and say plainly if any part used a fallback instead of the Meta API.
