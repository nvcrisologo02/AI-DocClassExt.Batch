"""Inventario del corpus validado: etiqueta por nombre, dedup por SHA256 y particiones.

Uso: python inventario.py [--corpus H:/Documentia/ParaNacho/Class] [--hilos 8]
Escribe CACHE/inventario.csv y CACHE/inventario_excluidos.csv.

Reanudable: cada fichero leído se registra al momento en CACHE/inventario_hashes.csv
(checkpoint). Si el proceso se interrumpe, la siguiente ejecución no vuelve a leer
los ficheros cuyo tamaño y mtime no han cambiado."""
from __future__ import annotations

import argparse
import csv
import hashlib
import io
import random
import re
import time
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

from pypdf import PdfReader

from comun import CACHE, CORPUS, GOLDEN_CSV

RE_TDN1 = re.compile(r"^[A-Z]{4}$")
RE_TDN2 = re.compile(r"^([A-Z]{4})-(\d{2})--")

CHECKPOINT_CSV = CACHE / "inventario_hashes.csv"
CHECKPOINT_CAMPOS = ["rel_path", "tamano", "mtime_ns", "sha256", "paginas"]


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


def huella(datos: bytes) -> tuple[str, int]:
    """Sha256 hex en minúsculas y número de páginas (-1 si no es un PDF legible),
    a partir de los bytes ya leídos una sola vez."""
    sha = hashlib.sha256(datos).hexdigest()
    try:
        paginas = len(PdfReader(io.BytesIO(datos)).pages)
    except Exception:
        paginas = -1
    return sha, paginas


def hay_que_releer(checkpoint: dict[str, dict], rel_path: str, tamano: int, mtime_ns: int) -> bool:
    """Dice si hace falta volver a leer un fichero dado el checkpoint ya cargado:
    solo se reutiliza si rel_path, tamaño y mtime_ns coinciden exactamente."""
    previo = checkpoint.get(rel_path)
    if previo is None:
        return True
    return previo["tamano"] != tamano or previo["mtime_ns"] != mtime_ns


def parsear_fila_checkpoint(fila: dict) -> dict | None:
    """Convierte una fila cruda del checkpoint (valores str, como los da csv.DictReader)
    a tipos. Devuelve None si la fila está truncada o corrupta (columnas que faltan,
    valores no numéricos, sha256 vacío) en vez de lanzar excepción: un corte de máquina
    a mitad de escritura no debe tirar el checkpoint entero."""
    try:
        sha = fila["sha256"]
        if not sha:
            return None
        return {
            "tamano": int(fila["tamano"]),
            "mtime_ns": int(fila["mtime_ns"]),
            "sha256": sha,
            "paginas": int(fila["paginas"]),
        }
    except (TypeError, ValueError, KeyError):
        return None


def cargar_checkpoint() -> dict[str, dict]:
    if not CHECKPOINT_CSV.exists():
        return {}
    checkpoint: dict[str, dict] = {}
    descartadas = 0
    with CHECKPOINT_CSV.open(encoding="utf-8-sig", newline="") as f:
        for fila in csv.DictReader(f, delimiter=";"):
            rel_path = fila.get("rel_path")
            valores = parsear_fila_checkpoint(fila)
            if not rel_path or valores is None:
                descartadas += 1
                continue
            checkpoint[rel_path] = valores
    if descartadas:
        print(f"filas de checkpoint descartadas: {descartadas}", flush=True)
    return checkpoint


def abrir_checkpoint_para_escritura():
    """Abre CACHE/inventario_hashes.csv en modo anexado, escribiendo cabecera si es nuevo."""
    CACHE.mkdir(parents=True, exist_ok=True)
    es_nuevo = not CHECKPOINT_CSV.exists() or CHECKPOINT_CSV.stat().st_size == 0
    f = CHECKPOINT_CSV.open("a", newline="", encoding="utf-8-sig")
    w = csv.DictWriter(f, CHECKPOINT_CAMPOS, delimiter=";")
    if es_nuevo:
        w.writeheader()
        f.flush()
    return f, w


def leer_y_procesar(pdf: Path, rel: str) -> dict:
    """Se ejecuta en el hilo del pool: una sola lectura de bytes, sha256 y páginas."""
    datos = pdf.read_bytes()
    sha, paginas = huella(datos)
    return {
        "rel_path": rel,
        "tamano": len(datos),
        "mtime_ns": pdf.stat().st_mtime_ns,
        "sha256": sha,
        "paginas": paginas,
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default=str(CORPUS))
    ap.add_argument("--hilos", type=int, default=8)
    args = ap.parse_args()
    raiz = Path(args.corpus)
    golden_rel = {r["rel_path"].lower() for r in csv.DictReader(GOLDEN_CSV.open(encoding="utf-8-sig"), delimiter=";")}

    checkpoint = cargar_checkpoint()
    print(f"checkpoint cargado: {len(checkpoint)} ficheros ya leídos", flush=True)

    candidatos = []  # (pdf, rel, tdn1, tdn2, golden)
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
        candidatos.append((pdf, rel, tdn1, tdn2, rel.lower() in golden_rel))

    total = len(candidatos)
    por_leer = []
    resultados: dict[str, dict] = {}
    for pdf, rel, tdn1, tdn2, golden in candidatos:
        st = pdf.stat()
        if hay_que_releer(checkpoint, rel, st.st_size, st.st_mtime_ns):
            por_leer.append((pdf, rel))
        else:
            resultados[rel] = checkpoint[rel]

    print(f"a leer: {len(por_leer)} de {total} (resto ya en checkpoint)", flush=True)

    checkpoint_f, checkpoint_w = abrir_checkpoint_para_escritura()
    procesados = len(resultados)
    fallidos: set[str] = set()
    gb_leidos = 0.0
    inicio = time.monotonic()
    try:
        with ThreadPoolExecutor(max_workers=args.hilos) as pool:
            futuros = {pool.submit(leer_y_procesar, pdf, rel): rel for pdf, rel in por_leer}
            for fut in as_completed(futuros):
                rel = futuros[fut]
                try:
                    r = fut.result()
                except Exception as exc:
                    # Fichero individual ilegible (SMB transitorio, permiso, movido):
                    # no se escribe en el checkpoint para que se reintente en la
                    # siguiente ejecución, y no aborta el resto del inventario.
                    fallidos.add(rel)
                    excluidos.append({"rel_path": rel, "motivo": "error_lectura"})
                    motivos["error_lectura"] += 1
                    print(f"error de lectura en {rel}: {type(exc).__name__}: {exc}", flush=True)
                    procesados += 1
                    continue
                resultados[rel] = r
                checkpoint[rel] = r
                checkpoint_w.writerow(r)
                checkpoint_f.flush()
                gb_leidos += r["tamano"] / (1024 ** 3)
                procesados += 1
                if procesados % 200 == 0:
                    minutos = (time.monotonic() - inicio) / 60
                    print(f"progreso: {procesados}/{total} | {gb_leidos:.2f} GB leídos | {minutos:.1f} min", flush=True)
    finally:
        checkpoint_f.close()

    minutos = (time.monotonic() - inicio) / 60
    print(f"lectura completa: {procesados}/{total} | {gb_leidos:.2f} GB leídos | {minutos:.1f} min", flush=True)
    if fallidos:
        print(f"ficheros con error de lectura (reintentables): {len(fallidos)}", flush=True)

    por_hash: dict[str, list[dict]] = defaultdict(list)
    for pdf, rel, tdn1, tdn2, golden in candidatos:
        if rel in fallidos:
            continue
        r = resultados[rel]
        por_hash[r["sha256"]].append(
            {"rel_path": rel, "tdn1": tdn1, "tdn2": tdn2 or "", "golden": golden, "paginas": r["paginas"]})

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
                      "golden": any(g["golden"] for g in grupo), "paginas": rep["paginas"]})

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
