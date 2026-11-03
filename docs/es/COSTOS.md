# Costo por conversación y por cita

Versión en inglés: [../COSTS.md](../COSTS.md).

Cada llamada al modelo registra **solo cifras**: proveedor, modelo, propósito (`agente`, `filtro`, `juez`…), tokens de entrada y salida, latencia, consultorio e id de la conversación. El contenido de los mensajes nunca se guarda en esta tabla (`UsosModelo`).

El registro se escribe en segundo plano (`ColaUso` → `GuardadoUsoHostedService`), así la respuesta al paciente nunca lo espera. Si la base no responde, se descartan los registros más viejos en lugar de llenar la memoria.

## Precios

Los precios **nunca van fijos en el código**: cambian por proveedor y con el tiempo. Se configuran por modelo, por millón de tokens, en `appsettings.json`:

```json
"Precios": {
  "Moneda": "USD",
  "Modelos": {
    "muse-spark-1.2": { "EntradaPorMillon": 0.0, "SalidaPorMillon": 0.0 }
  }
}
```

Reemplace los ceros con los precios vigentes de su proveedor. Como variables de entorno, el nombre del modelo lleva `-` y `.`, que `export` no acepta; use `env` o la pantalla de ajustes de su plataforma (Azure App Service, `environment:` de Docker):

```bash
env 'Precios__Modelos__muse-spark-1.2__EntradaPorMillon=<precio>' 'Precios__Modelos__muse-spark-1.2__SalidaPorMillon=<precio>' dotnet run --project src/DpmAgent.Api
```

Un modelo sin precio muestra su costo como desconocido (`—` / `n/a`) y aparece en "Sin precio configurado". Nunca cuenta como gratis.

## Panel

`GET /api/panel/costos?dias=30` (solo personal) y la pestaña **Costos** del panel muestran, para el periodo:

| Campo | Significado |
|---|---|
| `conversaciones` | Conversaciones con al menos una llamada al modelo |
| `citasAgendadas` | Citas agendadas por el agente (`cita_creada` y `oferta_aceptada` en la bitácora, actor `agente`) |
| `llamadas`, `tokensEntrada`, `tokensSalida` | Totales |
| `costoTotal` | Suma de los modelos que tienen precio |
| `costoPorConversacion` | `costoTotal / conversaciones` |
| `costoPorCita` | `costoTotal / citasAgendadas` |
| `porModelo` | Llamadas, tokens y costo por modelo |

## Evaluación

El informe del banco (`tools/DpmAgent.Eval`) usa los mismos precios y agrega el costo promedio por caso a las tablas por categoría e idioma. Ver [EVALUACION.md](EVALUACION.md).

## Pendiente

La transcripción de voz y la lectura del carnet llaman a sus propios endpoints y todavía no se cuentan en esta tabla.
