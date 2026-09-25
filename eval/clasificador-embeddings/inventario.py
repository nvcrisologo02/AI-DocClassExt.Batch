"""Inventario del corpus validado: etiqueta por nombre, dedup por SHA256 y particiones.

Uso: python inventario.py [--corpus H:/Documentia/ParaNacho/Class]
Escribe CACHE/inventario.csv y CACHE/inventario_excluidos.csv."""
from __future__ import annotations

import argparse
import csv
import random
import re
from collections import Counter, defaultdict
from pathlib import Path

from pypdf import PdfReader

from comun import CACHE, CORPUS, GOLDEN_CSV, sha256_fichero

RE_TDN1 = re.compile(r"^[A-Z]{4}$")
RE_TDN2 = re.compile(r"^([A-Z]{4})-(\d{2})--")


def parsear_etiqueta(carpeta: str, nombre: str):
    if not RE_TDN1.match(carpeta):
        return None, None, "carpeta_invalida"
    m = RE_TDN2.match(nombre)
    if not m:
        return carpeta, None, "sin_tdn2"
    if m.group(1) != carpeta:
        return None, None, "conflicto_carpeta"
    return carpeta, f"{m.group(1)}-{m.group(2)}", "ok"


def particionar(filas, semilla: int = 42, frac_cal: float = 0.2):
    """Golden fuera; el resto 80/20 estratificado por TDN1 y determinista."""
    out = [dict(f) for f in filas]
    por_tdn1 = defaultdict(list)
    for i, f in enumerate(out):
        if f["golden"]:
            f["particion"] = "golden"
        else:
            por_tdn1[f["tdn1"]].append(i)
    rnd = random.Random(semilla)
    for tdn1 in sorted(por_tdn1):
        idx = sorted(por_tdn1[tdn1], key=lambda i: out[i]["sha256"])
        rnd.shuffle(idx)
        n_cal = round(len(idx) * frac_cal)
        for k, i in enumerate(idx):
            out[i]["particion"] = "cal" if k < n_cal else "train"
    return out


def paginas(p: Path) -> int:
    try:
        return len(PdfReader(str(p)).pages)
    except Exception:
        return -1


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default=str(CORPUS))
    args = ap.parse_args()
    raiz = Path(args.corpus)
    golden_rel = {r["rel_path"].lower() for r in csv.DictReader(GOLDEN_CSV.open(encoding="utf-8-sig"), delimiter=";")}

    por_hash: dict[str, list[dict]] = defaultdict(list)
    excluidos, motivos = [], Counter()
    for pdf in sorted(raiz.glob("*/*")):
        if pdf.suffix.lower() != ".pdf":
            continue
        rel = f"{pdf.parent.name}/{pdf.name}"
        tdn1, tdn2, motivo = parsear_etiqueta(pdf.parent.name, pdf.name)
        motivos[motivo] += 1
        if tdn1 is None:
            excluidos.append({"rel_path": rel, "motivo": motivo})
            continue
        por_hash[sha256_fichero(pdf)].append(
            {"rel_path": rel, "tdn1": tdn1, "tdn2": tdn2 or "", "golden": rel.lower() in golden_rel, "ruta": pdf})

    filas = []
    for h, grupo in por_hash.items():
        etiquetas = {(g["tdn1"], g["tdn2"]) for g in grupo if g["tdn2"]} or {(grupo[0]["tdn1"], "")}
        if len({e[0] for e in etiquetas}) > 1 or len({e[1] for e in etiquetas if e[1]}) > 1:
            excluidos += [{"rel_path": g["rel_path"], "motivo": "conflicto_hash"} for g in grupo]
            motivos["conflicto_hash"] += len(grupo)
            continue
        tdn1, tdn2 = sorted(etiquetas)[-1]
        rep = next((g for g in grupo if g["golden"]), grupo[0])
        filas.append({"sha256": h, "rel_path": rep["rel_path"], "tdn1": tdn1, "tdn2": tdn2,
                      "golden": any(g["golden"] for g in grupo), "paginas": paginas(rep["ruta"])})

    filas = particionar(filas)
    CACHE.mkdir(parents=True, exist_ok=True)
    with (CACHE / "inventario.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["sha256", "rel_path", "tdn1", "tdn2", "particion", "paginas"], delimiter=";", extrasaction="ignore")
        w.writeheader()
        w.writerows(filas)
    with (CACHE / "inventario_excluidos.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["rel_path", "motivo"], delimiter=";")
        w.writeheader()
        w.writerows(excluidos)
    print("motivos:", dict(motivos))
    print("únicos por hash:", len(filas), "| particiones:", dict(Counter(f["particion"] for f in filas)))
    print("golden encontrados:", sum(f["golden"] for f in filas), "de", len(golden_rel))


if __name__ == "__main__":
    main()
