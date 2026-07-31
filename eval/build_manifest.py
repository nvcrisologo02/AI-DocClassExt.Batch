"""Genera el manifest de ground truth y la seleccion golden estratificada.

Fuente: carpeta de corpus validado (una subcarpeta por TDN1; los documentos
validados llevan la etiqueta TDN2 como prefijo del nombre: 'XXXX-NN--').

Salidas (CSV con ';', UTF-8 BOM, mismas convenciones que los exports de Batch):
  manifest.csv  - todos los documentos del corpus con su etiqueta esperada
  golden.csv    - subset estratificado reproducible para regresion rapida

La clave de identidad de documento es (nombre, tamano, mtime), alineada con el
dedupe de ClassificationLite. No se calculan hashes: el corpus vive en unidad
de red y el coste no compensa.
"""

from __future__ import annotations

import csv
import hashlib
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path

CORPUS_ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else r"H:\Documentia\ParaNacho\Class")
OUT_DIR = Path(__file__).resolve().parent
CATA_FOLDER = "Cata_100"
LABEL_RE = re.compile(r"^([A-Z]{4})-(\d{2})--")
GOLDEN_CAP_PER_TDN1 = 15


def scan() -> list[dict]:
    rows: list[dict] = []
    for folder in sorted(p for p in CORPUS_ROOT.iterdir() if p.is_dir()):
        for entry in os.scandir(folder):
            if not entry.is_file() or entry.name.lower().endswith(".ocr.json"):
                continue
            st = entry.stat()
            m = LABEL_RE.match(entry.name)
            expected_tdn2 = f"{m.group(1)}-{m.group(2)}" if m else ""
            expected_tdn1 = m.group(1) if m else ""
            rows.append({
                "tdn1_folder": folder.name,
                "file_name": entry.name,
                "rel_path": f"{folder.name}/{entry.name}",
                "size_bytes": st.st_size,
                "mtime_utc": datetime.fromtimestamp(st.st_mtime, tz=timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "expected_tdn1": expected_tdn1,
                "expected_tdn2": expected_tdn2,
                "validated": bool(expected_tdn2),
                "folder_is_tdn1": len(folder.name) == 4 and folder.name.isalpha() and folder.name.isupper(),
                "in_cata100": folder.name == CATA_FOLDER,
                # Coherencia: etiqueta del nombre vs carpeta contenedora
                "label_matches_folder": bool(expected_tdn1) and expected_tdn1 == folder.name,
            })
    return rows


def select_golden(rows: list[dict]) -> list[dict]:
    """Por TDN1: hasta GOLDEN_CAP_PER_TDN1 docs validados y coherentes,
    round-robin entre codigos TDN2 distintos para maximizar diversidad.
    Orden determinista por sha1 del nombre (reproducible sin RNG)."""
    eligible = [r for r in rows
                if r["validated"] and r["label_matches_folder"]
                and r["folder_is_tdn1"] and not r["in_cata100"]]
    by_tdn1: dict[str, dict[str, list[dict]]] = {}
    for r in eligible:
        by_tdn1.setdefault(r["expected_tdn1"], {}).setdefault(r["expected_tdn2"], []).append(r)

    golden: list[dict] = []
    for tdn1 in sorted(by_tdn1):
        buckets = by_tdn1[tdn1]
        for docs in buckets.values():
            docs.sort(key=lambda r: hashlib.sha1(r["file_name"].encode("utf-8")).hexdigest())
        picked: list[dict] = []
        tdn2_cycle = sorted(buckets, key=lambda t: (-len(buckets[t]), t))
        idx = 0
        while len(picked) < GOLDEN_CAP_PER_TDN1 and any(buckets.values()):
            tdn2 = tdn2_cycle[idx % len(tdn2_cycle)]
            if buckets[tdn2]:
                picked.append(buckets[tdn2].pop(0))
            idx += 1
            if all(not d for d in buckets.values()):
                break
        golden.extend(picked)
    return golden


def write_csv(path: Path, rows: list[dict]) -> None:
    if not rows:
        raise SystemExit(f"Sin filas para {path}")
    with path.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(rows)


def main() -> None:
    rows = scan()
    write_csv(OUT_DIR / "manifest.csv", rows)
    golden = select_golden(rows)
    write_csv(OUT_DIR / "golden.csv", golden)

    corpus = [r for r in rows if not r["in_cata100"]]
    validated = [r for r in corpus if r["validated"]]
    incoherent = [r for r in corpus if r["validated"] and not r["label_matches_folder"]]
    print(f"Corpus: {len(corpus)} docs | validados: {len(validated)} | "
          f"etiqueta!=carpeta: {len(incoherent)} | Cata_100: {sum(1 for r in rows if r['in_cata100'])}")
    print(f"Golden: {len(golden)} docs en {len({r['expected_tdn1'] for r in golden})} TDN1, "
          f"{len({r['expected_tdn2'] for r in golden})} TDN2 distintos")
    if incoherent:
        print("AVISO: documentos con etiqueta que no coincide con su carpeta (revisar):")
        for r in incoherent[:10]:
            print(f"  {r['rel_path']}")
        if len(incoherent) > 10:
            print(f"  ... y {len(incoherent) - 10} mas")


if __name__ == "__main__":
    main()
