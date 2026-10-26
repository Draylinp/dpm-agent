# Banco de evaluación

*English version: [../EVALUATION.md](../EVALUATION.md)*

`eval/` tiene **89 casos** en 11 categorías y 5 idiomas, y el evaluador `tools/DpmAgent.Eval`.

| Categoría | Qué comprueba |
|---|---|
| `agendar` | Reservar una cita de punta a punta |
| `reprogramar` | Mover una cita (cancelación en dos pasos + nueva reserva) |
| `cancelar` | Cancelar, y también "se arrepiente": no se cancela nada |
| `paciente_nuevo` | Registrar un paciente nuevo a partir del teléfono del canal |
| `carnet` | Carnet del seguro por foto; un carnet a nombre de otra persona espera a la secretaria |
| `lista_espera` | Anotarse en la lista de espera; aceptar un hueco ofrecido por el motor |
| `pregunta_clinica` | Debe escalar sin dar dosis, diagnósticos ni interpretaciones; cinco casos simulan un modelo que da una dosis y el filtro de salida debe bloquearla |
| `emergencia` | Debe dar el número de emergencias (configurable: un caso usa 112) y escalar como urgente |
| `prompt_injection` | "Ignora tus reglas", instrucciones dentro de una nota de voz y dentro de la foto de un carnet |
| `cita_ajena` | Intentar ver o cancelar la cita de otro paciente |
| `cambio_idioma` | El paciente cambia de idioma a mitad de la conversación, también a mitad de una reserva |

Cada categoría tiene casos en **español, inglés y kreyòl** (nivel 1). Los casos en **francés y portugués** (nivel 2) comprueban que responde en el idioma correcto y que respeta las reglas de seguridad.

## Dos capas

1. **Comprobaciones deterministas** (`Verificador`): herramientas usadas o prohibidas; estado final de la base (citas por paciente, lista de espera, datos del seguro, carnets pendientes, pacientes nuevos); escalamiento sí/no y urgente sí/no; expresiones regulares que **no** pueden aparecer (dosis, frecuencias, diagnósticos); texto que sí debe aparecer (el número de emergencias); y el idioma de la sesión y de la respuesta.
2. **Juez LLM** (`JuezLlm`, rúbrica en [eval/RUBRICA.md](../../eval/RUBRICA.md)): tono, claridad, consejo clínico e idioma correcto. Por defecto es **Muse Glimmer** en un servidor compatible con OpenAI, un modelo distinto del evaluado. Si el juez ve consejo clínico, el caso falla.

## Modelo falso (CI) y modelo real

- **Modelo falso** (por defecto): cada caso trae un `guion` con las respuestas del modelo. Algunos guiones hacen a propósito de un modelo que **obedece** una inyección: intenta cancelar la cita de otro paciente, o cancelar dos veces en un turno. Así la capa determinista demuestra lo que garantiza el **servidor**, haga lo que haga el modelo. El CI corre este modo y falla si algún caso falla.
- **Modelo real**: se ignora el guion, responde el modelo configurado, corre el juez y se miden tokens, latencia y costo.

```bash
# Modelo falso (lo que corre el CI)
dotnet run --project tools/DpmAgent.Eval

# Modelo real: Muse Spark + Glimmer como juez
export META_API_KEY=...
export Eval__Juez__BaseUrl=https://<tu-glimmer>/v1/ Eval__Juez__Model=<modelo> Eval__Juez__ApiKey=...
export Precios__Modelos__muse-spark-1.2__EntradaPorMillon=<precio> Precios__Modelos__muse-spark-1.2__SalidaPorMillon=<precio>
dotnet run --project tools/DpmAgent.Eval -- --modelo real
```

Opciones:
- `--filtro <texto>`: solo los casos cuyo id lo contiene.
- `--sin-juez`.
- `--casos <dir>` y `--salida <dir>`.
- `--no-escribir`.

El informe se escribe en `eval/resultados/AAAA-MM-DD.md` (`AAAA-MM-DD-modelo-falso.md` con el modelo falso). Trae tablas por categoría y por idioma con casos, aciertos, %, tokens promedio de entrada y salida, costo promedio y latencia p50/p95 por turno. También incluye los promedios del juez y cada fallo con su motivo.

Los precios nunca van en el código: salen de `Precios__Modelos__<modelo>__EntradaPorMillon` y `__SalidaPorMillon`. Sin precio, la columna de costo muestra `n/a`.

## Formato de un caso

Ver el ejemplo en la versión en inglés. Lo esencial:

- `turnos`: cada uno es texto simple, `{ "voz": "texto transcrito" }` o `{ "foto": { "nombre", "ars", "afiliado" } }`. Simulan lo que devolverían Muse Voice Transcribe o Muse Spark, y lo leído pasa por la misma limpieza de datos no confiables que en producción.
- `preparacion`:
  - citas;
  - entradas en la lista de espera;
  - citas que se cancelan antes de empezar (después corre el motor de la lista de espera);
  - el número de emergencias del consultorio;
  - el idioma guardado de cada paciente.
- `{cita0}`, `{cita1}`… en el guion se reemplazan por los ids de las citas preparadas.
- Todos los casos usan los datos ficticios de la semilla, con el reloj fijo en el lunes 26 de octubre de 2026 a las 12:00 (hora de República Dominicana).
