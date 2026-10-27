# Demo pública

Versión en inglés: [../DEMO.md](../DEMO.md).

El modo demo convierte el chat web en una página pública para jueces y visitantes. Todo lo que aparece es ficticio.

## Activarla

```bash
export Demo__Habilitado=true
# Opcional: crea el usuario "demo" del panel con esta clave y la muestra en la página
export Demo__ClavePanel="una-clave-larga-solo-para-la-demo"
dotnet run --project src/DpmAgent.Api
```

| Ajuste | Por defecto | Qué hace |
|---|---|---|
| `Demo__Habilitado` | `false` | Banner, botones de ejemplo, botón de reinicio y límite diario |
| `Demo__MensajesPorDiaPorIp` | `40` | Mensajes por día y por IP en `/api/chat`, `/api/chat/audio` y `/api/pacientes/carnet` (además del límite por minuto) |
| `Demo__ReiniciosPorHoraPorIp` | `10` | Reinicios por hora y por IP |
| `Demo__ClavePanel` | vacío | Clave del usuario `demo` del panel (rol secretaria). Vacío: no hay usuario demo |

## Qué ve el visitante

- Un banner **"Demo con datos ficticios"** y el botón **Reiniciar demo**.
- Ocho botones de ejemplo en el idioma de la interfaz (español o inglés): cita en inglés, en español y en kreyòl, pregunta clínica, emergencia, cancelar con lista de espera, "que me vean hoy" (primer hueco, el cupo de urgencia de las 4:30 p. m. o una solicitud de sobrecupo, siempre con el 911) y el médico pidiendo "Control para María en 3 meses" desde su número verificado.
- La traza del agente junto al chat: cada herramienta que el modelo decidió usar, con argumentos y resultado.

## Escenario listo

Al arrancar (y después de cada reinicio) el consultorio de demo tiene:

- María Rodríguez (ficticia, 809-555-0101) con una cita el próximo día hábil a las 9:00.
- José Martínez (ficticio, 829-555-0102) en la lista de espera de esa mañana.
- Una conversación web fija por número (`demo-` + número).
- Dos seguimientos indicados por la Dra. Ana Valdez (ficticia): el de María en 6 meses (espera; todavía no se envía nada) y el de José, que hoy entra en su ventana de contacto, así que el motor le escribe para escoger horario (se ve en su chat y en la pestaña Seguimientos del panel).
- El Dr. Luis Herrera (ficticio) con su número 849-555-0111 ya verificado. En el consultorio de demo, y solo con la demo activa, ese número también sirve desde el chat web para probar el canal del médico; fuera de la demo el canal del médico es solo por WhatsApp (`CanalMedico__PermitirChatWeb` lo cambia).
- Un cupo de urgencia a las 4:30 p. m. por médico y día hábil (reservado todo el día en la demo) y dos centros de emergencia ficticios, que se muestran fuera de horario.

Si se cancela la cita de María, el turno queda libre y el motor de agenda se lo ofrece solo a José. Como la conversación de José es fija, la oferta aparece en el chat en cuanto se escribe con su número (el botón "Pedir cita en kreyòl" lo hace).

Las conversaciones las comparten quienes usen el mismo número al mismo tiempo. Con datos ficticios no hay problema; **Reiniciar demo** deja todo limpio.

## Seguridad del reinicio

El reinicio solo borra datos del consultorio de la petición, y solo si está marcado `EsDemo` (el sembrado lo está). Si no, se niega. Nunca toca los usuarios del panel ni otros consultorios. Todo ocurre en una transacción y luego se vuelven a crear la semilla y el escenario. Cada reinicio queda en la bitácora.

Una base creada antes de esta versión queda con `EsDemo = false`: el reinicio se negará hasta que se marque a propósito.
