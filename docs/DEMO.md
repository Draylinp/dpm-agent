# Public demo

Spanish version: [es/DEMO.md](es/DEMO.md).

The demo mode turns the web chat into a public page for judges and visitors. Everything in it is fictional.

## Turn it on

```bash
export Demo__Habilitado=true
# Optional: creates the "demo" dashboard user with this password and shows it on the page
export Demo__ClavePanel="a-long-password-only-for-the-demo"
dotnet run --project src/DpmAgent.Api
```

| Setting | Default | What it does |
|---|---|---|
| `Demo__Habilitado` | `false` | Banner, example buttons, reset button and the daily limit |
| `Demo__MensajesPorDiaPorIp` | `40` | Messages per day and per IP on `/api/chat`, `/api/chat/audio` and `/api/pacientes/carnet` (on top of the per-minute limit) |
| `Demo__ReiniciosPorHoraPorIp` | `10` | Resets per hour and per IP |
| `Demo__ClavePanel` | empty | Password of the `demo` dashboard user (secretary role). Empty: no demo user |

## What the visitor sees

- A **"Demo with fictional data"** banner and a **Reset demo** button.
- Eight example buttons in the interface language (Spanish or English): book in English, book in Spanish, book in Haitian Creole, a clinical question, an emergency, cancelling with the waiting list, "see me today" (first slot, the 4:30 PM urgent slot or an extra-slot request, always with 911) and the doctor ordering "Follow-up for María in 3 months" from his verified number.
- The agent trace next to the chat: every tool the model decided to call, with its arguments and result.

## Ready-made scenario

On startup (and after each reset) the demo clinic has:

- María Rodríguez (fictional, 809-555-0101) with an appointment the next business day at 9:00.
- José Martínez (fictional, 829-555-0102) on the waiting list for that morning.
- One fixed web conversation per phone number (`demo-` + number).
- Two follow-ups ordered by Dra. Ana Valdez (fictional): María's in 6 months (it waits; nothing is sent yet) and José's, which enters its contact window today, so the engine writes to him to pick a time (it shows in his chat and in the dashboard's Follow-ups tab).
- Dr. Luis Herrera (fictional) with his number 849-555-0111 already verified. In the demo clinic, and only while the demo is on, that number also works from the web chat so you can try the doctor's channel; outside the demo the doctor's channel is WhatsApp only (`CanalMedico__PermitirChatWeb` changes it).
- An urgent slot at 4:30 PM per doctor and weekday (held all day in the demo) and two fictional emergency centers, shown out of hours.

Cancelling María's appointment frees the slot; the scheduling engine offers it to José automatically. Because José's conversation is fixed, the offer shows up in the chat as soon as you write with his number (the "Book in Haitian Creole" button does it).

The conversations are shared by everyone using the same number at the same time. That is fine for fictional data; use **Reset demo** to start clean.

## Reset safety

The reset only deletes data of the clinic of the request scope, and only if it is marked `EsDemo` (the seeded clinic is). It refuses otherwise. Dashboard users and other clinics are never touched. Everything happens in one transaction, then the seed and the scenario are recreated. Each reset is recorded in the audit log.

A database created before this version keeps `EsDemo = false`: the reset will refuse until you set it on purpose.
