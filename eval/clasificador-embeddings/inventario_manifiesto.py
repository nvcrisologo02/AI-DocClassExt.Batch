"""Inventario del corpus nuevo etiquetado por carpeta (CODIGO/CODIGO_n.ext).

Uso: python inventario_manifiesto.py [--corpus C:/temp/MVP/corpus-embeddings-v2]
         [--origen share-2026-11-03] [--hilos 8]
Opciones: --base (inventario base, por defecto CACHE/inventario.csv, sin tocarlo) y
--salida (CSV resultante, por defecto CACHE/inventario_v2.csv; los excluidos van al mismo
nombre con sufijo _excluidos)."""
from __future__ import annotations

import argparse
import csv
import random
import re
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from comun import CACHE
from inventario import huella

EXCLUIR_CARPETAS: frozenset[str] = frozenset({"ESIN-AI"})
EXCLUIR_FICHEROS: frozenset[str] = frozenset({"LIPR-06/LIPR-06_3.PDF", "LIPR-13/LIPR-13_31.pdf"})
MIN_DOCS = 5
MIN_HOLDOUT = 10
FRAC_HOLDOUT = 0.2
EXTENSIONES = {".pdf", ".jpg", ".jpeg", ".png"}
CAMPOS_V2 = ["sha256", "rel_path", "tdn1", "tdn2", "particion", "paginas", "origen"]

RE_CARPETA = re.compile(r"^([A-Z]{4})-([0-9A-Z]{2})(?:-.*)?$")


def etiqueta_desde_carpeta(carpeta: str) -> tuple[str, str] | None:
    m = RE_CARPETA.match(carpeta)
    if not m:
        return None
    return m.group(1), f"{m.group(1)}-{m.group(2)}"


def _leer(ruta: Path) -> tuple[str, int, int]:
    datos = ruta.read_bytes()
    sha, paginas = huella(datos)
    return sha, paginas, len(datos)


def recorrer(raiz: Path, hilos: int = 8) -> tuple[list[dict], list[dict]]:
    """Recorre raiz/CARPETA/fichero. Devuelve (filas, excluidos), ordenadas por rel_path."""
    excluidos: list[dict] = []
    candidatos: list[tuple[Path, str, str, str]] = []
    for ruta in sorted(p for p in raiz.glob("*/*") if p.is_file()):
        rel = f"{ruta.parent.name}/{ruta.name}"
        carpeta = ruta.parent.name
        if carpeta in EXCLUIR_CARPETAS:
            excluidos.append({"rel_path": rel, "motivo": "carpeta_excluida"})
        elif rel in EXCLUIR_FICHEROS:
            excluidos.append({"rel_path": rel, "motivo": "fichero_excluido"})
        elif etiqueta_desde_carpeta(carpeta) is None:
            excluidos.append({"rel_path": rel, "motivo": "carpeta_invalida"})
        elif ruta.suffix.lower() not in EXTENSIONES:
            excluidos.append({"rel_path": rel, "motivo": "extension"})
        else:
            tdn1, tdn2 = etiqueta_desde_carpeta(carpeta)
            candidatos.append((ruta, rel, tdn1, tdn2))
    with ThreadPoolExecutor(max_workers=hilos) as pool:
        resultados = list(pool.map(lambda c: _leer(c[0]), candidatos))
    filas = [{"rel_path": rel, "tdn1": tdn1, "tdn2": tdn2, "sha256": sha, "paginas": pag, "bytes": n}
             for (_, rel, tdn1, tdn2), (sha, pag, n) in zip(candidatos, resultados)]
    filas.sort(key=lambda f: f["rel_path"])
    return filas, excluidos


def dedup_por_hash(filas: list[dict]) -> tuple[list[dict], list[dict]]:
    """Mismo hash y misma etiqueta: se conserva la primera por rel_path; etiquetas distintas: todas fuera."""
    por_hash: dict[str, list[dict]] = defaultdict(list)
    for f in sorted(filas, key=lambda f: f["rel_path"]):
        por_hash[f["sha256"]].append(f)
    out, excluidos = [], []
    for grupo in por_hash.values():
        if len({g["tdn2"] for g in grupo}) > 1:
            excluidos += [{"rel_path": g["rel_path"], "motivo": "conflicto_hash"} for g in grupo]
            continue
        out.append(grupo[0])
        excluidos += [{"rel_path": g["rel_path"], "motivo": "duplicado"} for g in grupo[1:]]
    out.sort(key=lambda f: f["rel_path"])
    return out, excluidos


def particionar_v2(filas: list[dict], semilla: int = 42) -> tuple[list[dict], list[dict]]:
    """Por tdn2: n < MIN_DOCS fuera; n >= MIN_HOLDOUT aparta round(n*FRAC_HOLDOUT) a hold2; resto train."""
    por_clase: dict[str, list[dict]] = defaultdict(list)
    for f in filas:
        por_clase[f["tdn2"]].append(f)
    rnd = random.Random(semilla)
    out, excluidos = [], []
    for tdn2 in sorted(por_clase):
        grupo = sorted(por_clase[tdn2], key=lambda f: f["sha256"])
        if len(grupo) < MIN_DOCS:
            excluidos += [{"rel_path": g.get("rel_path", ""), "motivo": "bajo_minimo"} for g in grupo]
            continue
        rnd.shuffle(grupo)
        n_hold = round(len(grupo) * FRAC_HOLDOUT) if len(grupo) >= MIN_HOLDOUT else 0
        for k, g in enumerate(grupo):
            out.append({**g, "particion": "hold2" if k < n_hold else "train"})
    return out, excluidos


def combinar(v1: list[dict], nuevas: list[dict], origen: str) -> list[dict]:
    """Filas base intactas (conservan su origen; corpus-v1 si no lo traen) seguidas de las nuevas ya filtradas."""
    return ([{**f, "origen": f.get("origen") or "corpus-v1"} for f in v1]
            + [{**f, "origen": origen} for f in nuevas])


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default="C:/temp/MVP/corpus-embeddings-v2")
    ap.add_argument("--origen", default="share-2026-11-03")
    ap.add_argument("--hilos", type=int, default=8)
    ap.add_argument("--base", default=str(CACHE / "inventario.csv"))
    ap.add_argument("--salida", default=str(CACHE / "inventario_v2.csv"))
    args = ap.parse_args()

    with Path(args.base).open(encoding="utf-8-sig", newline="") as f:
        v1 = list(csv.DictReader(f, delimiter=";"))
    hashes_v1 = {r["sha256"] for r in v1}

    filas, excluidos = recorrer(Path(args.corpus), args.hilos)
    filas, exc = dedup_por_hash(filas)
    excluidos += exc
    nuevas = [f for f in filas if f["sha256"] not in hashes_v1]
    excluidos += [{"rel_path": f["rel_path"], "motivo": "ya_en_v1"} for f in filas if f["sha256"] in hashes_v1]
    nuevas, exc = particionar_v2(nuevas)
    excluidos += exc

    salida = combinar(v1, nuevas, args.origen)
    ruta_salida = Path(args.salida)
    ruta_excl = ruta_salida.with_name(f"{ruta_salida.stem}_excluidos{ruta_salida.suffix}")
    ruta_salida.parent.mkdir(parents=True, exist_ok=True)
    with ruta_salida.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, CAMPOS_V2, delimiter=";", extrasaction="ignore")
        w.writeheader()
        w.writerows(salida)
    with ruta_excl.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["rel_path", "motivo"], delimiter=";")
        w.writeheader()
        w.writerows(excluidos)
    print("motivos:", dict(Counter(e["motivo"] for e in excluidos)))
    print("particiones nuevas:", dict(Counter(f["particion"] for f in nuevas)))
    print("por tdn2 (nuevas):", dict(sorted(Counter(f["tdn2"] for f in nuevas).items())))


if __name__ == "__main__":
    main()
