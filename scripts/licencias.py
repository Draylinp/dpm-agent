#!/usr/bin/env python3
"""Genera THIRD_PARTY_NOTICES.md con todas las dependencias y sus licencias.

Uso:
  python3 scripts/licencias.py              # escribe THIRD_PARTY_NOTICES.md
  python3 scripts/licencias.py --verificar  # falla si hay una licencia desconocida o no permitida,
                                            # o si el archivo del repo no está al día (lo usa el CI)

Lee los paquetes NuGet con `dotnet list package --include-transitive` (requiere restore) y la licencia
de cada .nuspec en la caché de NuGet. El resto (imágenes de contenedor, paquetes de Python de deploy/,
herramientas de desarrollo, modelos) está declarado abajo a mano.
"""
import json
import os
import re
import subprocess
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SALIDA = os.path.join(RAIZ, "THIRD_PARTY_NOTICES.md")

# Licencias compatibles con distribuir el proyecto bajo MIT.
PERMITIDAS = {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "PostgreSQL", "ISC", "0BSD", "Zlib", "Unlicense"}

# Paquetes antiguos que solo declaran la licencia por URL.
POR_URL = {
    "https://github.com/dotnet/corefx/blob/master/LICENSE.TXT": "MIT",
    "https://raw.githubusercontent.com/xunit/xunit/master/license.txt": "Apache-2.0",
}

# Componentes que no son paquetes NuGet. (nombre, versión o etiqueta, licencia, dónde se usa)
CONTENEDORES = [
    ("mcr.microsoft.com/dotnet/sdk", "8.0", "MIT", "Dockerfile (compilación)"),
    ("mcr.microsoft.com/dotnet/aspnet", "8.0", "MIT", "Dockerfile (imagen final)"),
    ("postgres", "16", "PostgreSQL", "docker-compose.yml, CI"),
    ("vllm/vllm-openai", "v0.10.1", "Apache-2.0", "docker-compose.yml (perfil glimmer, opcional)"),
]
PYTHON_DEPLOY = [
    ("modal", "-", "Apache-2.0", "deploy/modal (servir Muse Glimmer / Llama Guard, opcional)"),
    ("vllm", "-", "Apache-2.0", "deploy/modal"),
    ("huggingface_hub", "-", "Apache-2.0", "deploy/modal"),
    ("hf_transfer", "-", "Apache-2.0", "deploy/modal"),
]
# Herramientas de desarrollo y de CI: no se distribuyen con la app.
DESARROLLO = [
    ("Playwright (Node)", "preinstalado en CI", "Apache-2.0", "scripts/generar_carnet.mjs, pruebas de accesibilidad"),
    ("axe-core", "4.10.2", "MPL-2.0", "pruebas de accesibilidad en CI (no se incluye en la app)"),
    ("Mermaid (GitHub)", "-", "MIT", "diagrama del README, lo dibuja GitHub"),
]
PERMITIDAS_DESARROLLO = PERMITIDAS | {"MPL-2.0"}

FRONTEND = """El chat web, la demo, el panel y el aviso de privacidad (`src/DpmAgent.Api/wwwroot/`) no usan bibliotecas,
fuentes ni íconos de terceros: todo el JavaScript y el CSS son del proyecto, la letra es la del sistema
(`system-ui`) y los íconos son caracteres Unicode (emoji) que dibuja la fuente del sistema del usuario."""

IMAGENES = """| Archivo | Origen |
|---|---|
| `docs/img/demo-en.png`, `docs/img/demo-es.png` | Capturas de la propia app con datos ficticios |
| `src/DpmAgent.Api/wwwroot/demo/carnet-ficticio.png` | Dibujado por `scripts/generar_carnet.mjs` (carnet inventado, sin logos ni marcas) |"""

MODELOS = """| Modelo | Uso | Licencia / términos |
|---|---|---|
| Muse Spark, Muse Voice Transcribe | Por la Meta Model API (servicio) | Términos de la Meta Model API; no se distribuyen pesos |
| Muse Glimmer | Opcional, pesos abiertos servidos por el usuario con vLLM | Licencia que Meta publica con los pesos; el repo no incluye pesos |
| Llama Guard | Opcional, filtro de salida servido por el usuario | Licencia de Llama que Meta publica con los pesos; el repo no incluye pesos |"""


def paquetes_nuget():
    salida = subprocess.run(
        ["dotnet", "list", os.path.join(RAIZ, "DpmAgent.sln"), "package", "--include-transitive", "--format", "json"],
        check=True, capture_output=True, text=True).stdout
    datos = json.loads(salida)
    usados = {}
    for proyecto in datos["projects"]:
        nombre = os.path.basename(proyecto["path"]).replace(".csproj", "")
        for fw in proyecto.get("frameworks", []):
            for clave in ("topLevelPackages", "transitivePackages"):
                for p in fw.get(clave, []):
                    usados.setdefault((p["id"], p["resolvedVersion"]), set()).add(nombre)
    return usados


def licencia(id_, version):
    cache = os.environ.get("NUGET_PACKAGES") or os.path.expanduser("~/.nuget/packages")
    ruta = os.path.join(cache, id_.lower(), version, id_.lower() + ".nuspec")
    if not os.path.exists(ruta):
        return None
    texto = open(ruta, encoding="utf-8-sig").read()
    m = re.search(r'<license type="expression">([^<]+)</license>', texto)
    if m:
        return m.group(1).strip()
    m = re.search(r"<licenseUrl>([^<]+)</licenseUrl>", texto)
    return POR_URL.get(m.group(1).strip()) if m else None


def tabla(filas, encabezado):
    lineas = ["| " + " | ".join(encabezado) + " |", "|" + "---|" * len(encabezado)]
    lineas += ["| " + " | ".join(f) + " |" for f in filas]
    return "\n".join(lineas)


def generar():
    problemas = []
    filas = []
    for (id_, version), proyectos in sorted(paquetes_nuget().items(), key=lambda x: x[0][0].lower()):
        lic = licencia(id_, version)
        if lic is None:
            problemas.append(f"{id_} {version}: licencia desconocida")
        elif not set(re.split(r"\s+(?:OR|AND)\s+", lic)) & PERMITIDAS or (" AND " in lic and not set(lic.split(" AND ")) <= PERMITIDAS):
            problemas.append(f"{id_} {version}: licencia no permitida ({lic})")
        filas.append((f"`{id_}`", version, lic or "**desconocida**", ", ".join(sorted(proyectos))))

    for nombre, version, lic, uso in CONTENEDORES + PYTHON_DEPLOY:
        if lic not in PERMITIDAS:
            problemas.append(f"{nombre}: licencia no permitida ({lic})")
    for nombre, version, lic, uso in DESARROLLO:
        if lic not in PERMITIDAS_DESARROLLO:
            problemas.append(f"{nombre}: licencia no permitida para desarrollo ({lic})")

    md = f"""# Third-party notices / Avisos de terceros

DPM Agent is released under the MIT license (see `LICENSE`). This file lists every third-party component it
uses and its license. It is generated by `scripts/licencias.py`; CI fails if a dependency has an unknown
license or one that is not compatible with MIT, or if this file is out of date.

DPM Agent se publica con licencia MIT (ver `LICENSE`). Este archivo enumera cada componente de terceros que usa
y su licencia. Lo genera `scripts/licencias.py`; el CI falla si una dependencia tiene una licencia desconocida o
incompatible con MIT, o si este archivo no está al día.

Allowed licenses / Licencias permitidas: {", ".join(sorted(PERMITIDAS))}.

## NuGet packages ({len(filas)}, including transitive / incluidos los transitivos)

{tabla(filas, ["Package", "Version", "License", "Used by"])}

## Container images / Imágenes de contenedor

{tabla([(f"`{n}`", v, l, u) for n, v, l, u in CONTENEDORES], ["Image", "Tag", "License", "Used in"])}

## Python packages for optional deployment / Paquetes de Python para el despliegue opcional

{tabla([(f"`{n}`", v, l, u) for n, v, l, u in PYTHON_DEPLOY], ["Package", "Version", "License", "Used in"])}

## Development and CI tools, not distributed / Herramientas de desarrollo y CI, no se distribuyen

{tabla([(n, v, l, u) for n, v, l, u in DESARROLLO], ["Tool", "Version", "License", "Used in"])}

## Front end, fonts and icons / Interfaz, fuentes e íconos

{FRONTEND}

## Images / Imágenes

{IMAGENES}

## AI models / Modelos de IA

{MODELOS}

## Data / Datos

Every patient, doctor, clinic and health insurer (ARS) in the repository, the demo and the evaluation bank is
fictional ("ARS Ejemplo", "ARS Demo Salud", "Consultorio Ejemplo (ficticio)"). `scripts/verificar_ficticios.py`
fails CI if a real Dominican ARS, clinic or hospital name appears.

Todos los pacientes, médicos, consultorios y ARS del repositorio, la demo y el banco de evaluación son
ficticios. `scripts/verificar_ficticios.py` hace fallar el CI si aparece el nombre de una ARS, clínica u
hospital real de República Dominicana.
"""
    return md, problemas


def main():
    md, problemas = generar()
    if "--verificar" in sys.argv:
        actual = open(SALIDA, encoding="utf-8").read() if os.path.exists(SALIDA) else ""
        if actual != md:
            problemas.append("THIRD_PARTY_NOTICES.md no está al día: ejecute python3 scripts/licencias.py")
        for p in problemas:
            print("ERROR:", p)
        if problemas:
            sys.exit(1)
        print("Licencias verificadas.")
        return
    with open(SALIDA, "w", encoding="utf-8") as f:
        f.write(md)
    for p in problemas:
        print("AVISO:", p)
    print("Escrito", SALIDA)


if __name__ == "__main__":
    main()
