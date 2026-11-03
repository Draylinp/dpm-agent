# Filtro de seguridad de salida

*English version: [../OUTPUT-FILTER.md](../OUTPUT-FILTER.md)*

Antes de que cualquier respuesta del agente llegue al paciente (chat web, WhatsApp, nota de voz o foto del carnet), `IFiltroSalida` comprueba que **no contenga consejo médico**. Eso incluye:
- medicamentos, dosis o frecuencias;
- tratamientos;
- interpretación de síntomas o resultados;
- diagnósticos;
- decir si algo es grave o no.

Los textos fijos del sistema (recordatorios, ofertas, errores) son plantillas y son seguros por construcción.

## Capas

| Capa | Qué es | Costo |
|---|---|---|
| 0, reglas (siempre activa) | Expresiones regulares en español, inglés, kreyòl, francés y portugués: dosis (`5 ml`, `500 mg`), frecuencias (`cada 8 horas`, `every 8 hours`, `chak 8 è`), cantidades de pastillas y "tome/dele + medicamento" | Gratis, microsegundos |
| 1, clasificador (configurable) | **Llama Guard** (modelo de seguridad de Meta con pesos abiertos) o, si no está desplegado, el modelo configurado con un prompt clasificador estricto y salida JSON | Una llamada corta extra por respuesta |

Valores de `FiltroSalida__Proveedor`:
- **`LlamaGuard`**: Llama Guard servido por vLLM con endpoint compatible con OpenAI (`deploy/modal/llama_guard_vllm.py`). Recibe el turno (paciente y agente) y responde `safe` o `unsafe` con las categorías violadas; la categoría **S6 "Specialized Advice"** cubre el consejo médico. Requiere `FiltroSalida__BaseUrl`, `__Model` y `__ApiKey`.
- **`Modelo`** (por defecto): el modelo principal (Muse Spark) como clasificador.
- **`Reglas`**: solo reglas (se usa con el modelo guionado en pruebas y CI).

Desde el entorno donde se construyó esto no se pudo consultar la documentación de Meta. Por eso la integración apunta a Llama Guard con pesos abiertos sobre vLLM, que es la forma documentada de correrlo. Si Meta ofrece Llama Guard por su API con formato compatible con OpenAI, basta con apuntar `FiltroSalida__BaseUrl` a esa API.

Si el clasificador falla (está caído o devuelve algo ilegible):
- `FiltroSalida__BloquearSiFalla=false` (por defecto) deja pasar la respuesta, porque la capa 0 ya la revisó, y registra una advertencia.
- `true` la bloquea. Bloquear todo mientras el clasificador está caído dejaría a los pacientes sin atención.

## Cuando bloquea

1. Se reemplaza la respuesta por un mensaje seguro en el idioma del paciente ("eso lo debe responder el personal médico; ya pasé su mensaje al consultorio; si es una emergencia, llame al {número del consultorio}"). Los idiomas de nivel 2 reciben el texto en inglés.
2. Se crea un escalamiento con el mensaje original del paciente y resúmenes en español e inglés.
3. La bitácora guarda el texto bloqueado, el motivo y la capa que decidió (`reglas`, `llama-guard` o `modelo`).
4. El texto bloqueado sale del historial de la conversación: el modelo no lo ve en el turno siguiente.
5. La traza del chat web muestra el paso `filtro_salida`.

## Cómo se mide

El banco de evaluación cuenta los bloqueos por categoría y por idioma. Los bloqueos en categorías donde no se espera contenido clínico se marcan como **posibles falsos positivos**. Cinco casos, uno por idioma, simulan un modelo que da una dosis a propósito, y el filtro debe bloquear todos. Ver [EVALUACION.md](EVALUACION.md).
