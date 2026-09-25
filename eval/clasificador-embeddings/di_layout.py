"""DI Layout de DEV sobre los PDF sin markdown en BD. Dos modos:
  --estimar   solo recuento de documentos y páginas facturables (no llama a DI)
  --lanzar    llama a DI; solo tras autorización explícita del usuario
Uso: python di_layout.py --estimar | --lanzar [--max-paginas 5] [--solo-golden]"""
from __future__ import annotations

import argparse
import base64
import csv
import time

from comun import CACHE, CORPUS, DI_ENDPOINT, sesion_http
from texto import guardar_md

API = "2024-11-30"


def paginas_facturables(paginas: int, max_paginas: int) -> int:
    return max_paginas if paginas < 0 else min(paginas, max_paginas)


def analizar(s, pdf_bytes: bytes, max_paginas: int) -> str:
    url = (f"{DI_ENDPOINT}/documentintelligence/documentModels/prebuilt-layout:analyze"
           f"?api-version={API}&outputContentFormat=markdown&pages=1-{max_paginas}")
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
    ok = err = 0
    for p in pend:
        try:
            md = analizar(s, (CORPUS / p["rel_path"]).read_bytes(), args.max_paginas)
            if md.strip():
                guardar_md(p["sha256"], md)
                idx[p["sha256"]].update(origen_texto="di_dev", caracteres=str(len(md)))
                ok += 1
        except Exception as e:  # se registra y se sigue; el documento queda sin_texto
            err += 1
            print("error", p["rel_path"], type(e).__name__)
        if (ok + err) % 100 == 0:
            s = sesion_http()  # renueva el token en ejecuciones largas
    with origen_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["sha256", "origen_texto", "caracteres"], delimiter=";")
        w.writeheader()
        w.writerows(filas)
    print(f"DI ok {ok} | errores {err}")


if __name__ == "__main__":
    main()
