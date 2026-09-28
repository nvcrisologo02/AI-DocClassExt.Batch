"""DI Layout de DEV sobre los PDF sin markdown en BD. Dos modos:
  --estimar   solo recuento de documentos y páginas facturables (no llama a DI)
  --lanzar    llama a DI; solo tras autorización explícita del usuario
Uso: python di_layout.py --estimar | --lanzar [--max-paginas 5] [--solo-golden]"""
from __future__ import annotations

import argparse
import base64
import csv
import os
import time

from comun import CACHE, CORPUS, DI_ENDPOINT, sesion_http
from texto import guardar_md, leer_md, ruta_md

API = "2024-11-30"


def paginas_facturables(paginas: int, max_paginas: int) -> int:
    return max_paginas if paginas < 0 else min(paginas, max_paginas)


def rango_paginas(paginas: int, max_paginas: int) -> str:
    return f"1-{paginas_facturables(paginas, max_paginas)}"


def ya_en_cache(sha: str) -> bool:
    """True si el markdown de sha ya está en CACHE/md: reanudar --lanzar no debe
    volver a facturar DI por un documento cuyo .md.gz ya existe (venga de un checkpoint
    anterior marcado di_dev o de una reutilización previa), da igual qué diga
    origen_texto en texto_origen.csv."""
    return ruta_md(sha).exists()


def escribir_origen(ruta, filas) -> None:
    """Escritura atómica de texto_origen.csv: fichero temporal en el mismo directorio
    y os.replace, para no dejar el CSV a medias si la ejecución se corta a mitad."""
    tmp = ruta.with_name(ruta.name + ".tmp")
    with tmp.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["sha256", "origen_texto", "caracteres"], delimiter=";")
        w.writeheader()
        w.writerows(filas)
    os.replace(tmp, ruta)


def analizar(s, pdf_bytes: bytes, rango: str) -> str:
    url = (f"{DI_ENDPOINT}/documentintelligence/documentModels/prebuilt-layout:analyze"
           f"?api-version={API}&outputContentFormat=markdown&pages={rango}")
    r = s.post(url, json={"base64Source": base64.b64encode(pdf_bytes).decode()}, timeout=120)
    r.raise_for_status()
    op = r.headers["Operation-Location"]
    for _ in range(120):
        time.sleep(2)
        j = s.get(op, timeout=60).json()
        if j["status"] == "succeeded":
            return j["analyzeResult"]["content"]
        if j["status"] == "failed":
            raise RuntimeError(j.get("error"))
    raise TimeoutError(op)


def main() -> None:
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--estimar", action="store_true")
    g.add_argument("--lanzar", action="store_true")
    ap.add_argument("--max-paginas", type=int, default=5)
    ap.add_argument("--solo-golden", action="store_true")
    args = ap.parse_args()

    pend = list(csv.DictReader((CACHE / "pendientes_di.csv").open(encoding="utf-8-sig"), delimiter=";"))
    if args.solo_golden:
        inv = {r["sha256"]: r["particion"] for r in csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";")}
        pend = [p for p in pend if inv.get(p["sha256"]) == "golden"]
    total = sum(paginas_facturables(int(p["paginas"]), args.max_paginas) for p in pend)
    print(f"documentos: {len(pend)} | páginas facturables (recorte {args.max_paginas}): {total}")
    if args.estimar:
        return

    s = sesion_http()
    origen_csv = CACHE / "texto_origen.csv"
    filas = list(csv.DictReader(origen_csv.open(encoding="utf-8-sig"), delimiter=";"))
    idx = {f["sha256"]: f for f in filas}
    total_pend = len(pend)
    ok = err = vacios = reutilizados = 0
    ultima_renovacion = time.monotonic()
    for p in pend:
        sha = p["sha256"]
        fila = idx[sha]
        if ya_en_cache(sha):
            md = leer_md(sha)
            fila.update(origen_texto="di_dev", caracteres=str(len(md)))
            reutilizados += 1
        else:
            try:
                md = analizar(s, (CORPUS / p["rel_path"]).read_bytes(), rango_paginas(int(p["paginas"]), args.max_paginas))
                if md.strip():
                    guardar_md(sha, md)
                    fila.update(origen_texto="di_dev", caracteres=str(len(md)))
                    ok += 1
                else:
                    vacios += 1
            except Exception as e:  # se registra y se sigue; el documento queda sin_texto
                err += 1
                print("error", p["rel_path"], type(e).__name__)
        procesados = ok + reutilizados + err + vacios
        if procesados % 100 == 0:
            print(
                f"progreso {procesados}/{total_pend} | ok {ok} | reutilizados {reutilizados} "
                f"| vacíos {vacios} | errores {err}",
                flush=True,
            )
            escribir_origen(origen_csv, filas)  # checkpoint: como mucho se pierden 100 marcas si se corta
            ahora = time.monotonic()
            if ahora - ultima_renovacion > 30 * 60:
                s = sesion_http()  # renueva el token en ejecuciones largas (cada 30 minutos)
                ultima_renovacion = ahora
    escribir_origen(origen_csv, filas)
    print(f"DI ok {ok} | reutilizados {reutilizados} | vacíos {vacios} | errores {err}")


if __name__ == "__main__":
    main()
