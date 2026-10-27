#!/usr/bin/env python3
"""Falla si en el repositorio aparece el nombre de una ARS, clínica u hospital real de República Dominicana.

Todos los datos del repo, la demo y el banco de evaluación deben ser ficticios ("ARS Ejemplo",
"Consultorio Ejemplo (ficticio)"). La lista no es exhaustiva: es una red de seguridad para los nombres
más comunes. Uso: python3 scripts/verificar_ficticios.py
"""
import os
import re
import subprocess
import sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Con prefijo cuando la palabra sola es común en un texto ("humano", "futuro", "universal").
PROHIBIDOS = [
    r"ARS\s+(Humano|Universal|Palic|Monumental|Renacer|Semma|Futuro|Yunen|Reservas|Simag|Asemap|Plan Salud|Meta Salud|GMA|APS|CMD|Salud Segura|Primera)",
    r"Primera\s+ARS", r"\bSe?NaSa\b", r"Mapfre\s+Salud", r"Humano\s+Seguros",
    r"\bCEDIMAT\b", r"Plaza\s+de\s+la\s+Salud", r"Cl[ií]nica\s+Abreu", r"Corazones\s+Unidos", r"Dar[ií]o\s+Contreras",
    r"\bHOMS\b", r"Uni[oó]n\s+M[eé]dica", r"Cl[ií]nica\s+Corominas", r"Centro\s+M[eé]dico\s+Moderno", r"Robert\s+Reid\s+Cabral",
    r"Ney\s+Arias\s+Lora", r"Cl[ií]nica\s+Gazcue", r"Centro\s+M[eé]dico\s+Dominicano", r"Vinicio\s+Calventi", r"Marcelino\s+V[eé]lez",
    r"Hospital\s+Metropolitano\s+de\s+Santiago", r"Centro\s+de\s+Diagn[oó]stico\s+y\s+Medicina\s+Avanzada", r"Cl[ií]nica\s+Independencia",
    r"Centro\s+M[eé]dico\s+Cibao", r"Hospital\s+General\s+Regional", r"Cl[ií]nica\s+Cruz\s+Jiminian", r"Hospital\s+Salvador\s+B\.?\s+Gautier",
]
PATRON = re.compile("|".join(PROHIBIDOS), re.IGNORECASE)
TEXTO = (".cs", ".json", ".md", ".html", ".js", ".mjs", ".py", ".yml", ".yaml", ".sh", ".example", ".txt", ".csv")


def main():
    archivos = subprocess.run(["git", "ls-files"], cwd=RAIZ, check=True, capture_output=True, text=True).stdout.split()
    hallazgos = []
    for ruta in archivos:
        if not ruta.endswith(TEXTO) or ruta.endswith("verificar_ficticios.py") or "/Migraciones/" in ruta:
            continue
        with open(os.path.join(RAIZ, ruta), encoding="utf-8", errors="ignore") as f:
            for n, linea in enumerate(f, 1):
                if m := PATRON.search(linea):
                    hallazgos.append(f"{ruta}:{n}: {m.group(0)}")
    for h in hallazgos:
        print("ERROR: nombre real en", h)
    if hallazgos:
        sys.exit(1)
    print(f"Datos ficticios verificados ({len(archivos)} archivos).")


if __name__ == "__main__":
    main()
