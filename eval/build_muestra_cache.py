"""Genera la muestra fija de 50 documentos para la medicion de cache de luna (AB#101413).

Lee eval/golden.csv (separador ';', UTF-8 con BOM), se queda con las filas validadas,
toma las 10 familias expected_tdn1 con mas filas (desempate alfabetico) y, dentro de
cada una, los 5 primeros file_name por orden alfabetico.

Salidas (junto a este script):
  golden-muestra-cache.txt     50 nombres, uno por linea, sin comentarios
  golden-muestra-cache.sha256  '<sha256hex>  golden.csv  generado <YYYY-MM-DD>'

Uso: python -I eval/build_muestra_cache.py
"""
import csv
import datetime
import hashlib
from collections import defaultdict
from pathlib import Path

FAMILIAS = 10
POR_FAMILIA = 5

aqui = Path(__file__).resolve().parent
golden = aqui / "golden.csv"

por_familia = defaultdict(list)
with golden.open(encoding="utf-8-sig", newline="") as f:
    for fila in csv.DictReader(f, delimiter=";"):
        if fila["validated"] == "True":
            por_familia[fila["expected_tdn1"]].append(fila["file_name"])

top = sorted(por_familia, key=lambda t: (-len(por_familia[t]), t))[:FAMILIAS]
nombres = []
for tdn1 in top:
    nombres.extend(sorted(por_familia[tdn1])[:POR_FAMILIA])

(aqui / "golden-muestra-cache.txt").write_text("\n".join(nombres) + "\n", encoding="utf-8", newline="\n")
digest = hashlib.sha256(golden.read_bytes()).hexdigest()
fecha = datetime.date.today().isoformat()
(aqui / "golden-muestra-cache.sha256").write_text(
    f"{digest}  golden.csv  generado {fecha}\n", encoding="utf-8", newline="\n")
print(f"{len(nombres)} nombres de {len(top)} familias: {', '.join(top)}")
