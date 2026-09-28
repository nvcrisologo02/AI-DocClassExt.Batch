"""Enfoque C (prompt compacto con logprobs) sobre TODO el conjunto 'cal', reanudable.

Reutiliza logprobs_gpt.correr y prompt_sistema sin modificarlos. Guarda cada 50 documentos
en eval/runs/cal-logprobs-gpt/resultados.csv (clave sha256); al relanzar solo procesa los
que faltan. Solo inferencia en DEV.
Uso: python logprobs_cal.py"""
from __future__ import annotations

import csv

from comun import CACHE, EVAL, sesion_http
from logprobs_gpt import correr, prompt_sistema

SALIDA = EVAL / "runs" / "cal-logprobs-gpt"
CSV = SALIDA / "resultados.csv"
CAMPOS = ["sha256", "rel_path", "exp_tdn1", "C_tdn1", "C_conf", "error"]
BLOQUE = 50


def main() -> None:
    SALIDA.mkdir(parents=True, exist_ok=True)
    inv = list(csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";"))
    cal = sorted((r for r in inv if r["particion"] == "cal"), key=lambda r: r["sha256"])
    hechos = set()
    if CSV.exists():
        hechos = {r["sha256"] for r in csv.DictReader(CSV.open(encoding="utf-8-sig"), delimiter=";")}
    faltan = [r for r in cal if r["sha256"] not in hechos]
    print(f"cal {len(cal)} | hechos {len(hechos)} | faltan {len(faltan)}", flush=True)
    nuevo = not CSV.exists()
    sistema = prompt_sistema()
    with CSV.open("a", newline="", encoding="utf-8-sig" if nuevo else "utf-8") as fh:
        w = csv.DictWriter(fh, CAMPOS, delimiter=";")
        if nuevo:
            w.writeheader()
        for i in range(0, len(faltan), BLOQUE):
            lote = faltan[i:i + BLOQUE]
            s = sesion_http()  # token nuevo por bloque; clasificar además renueva ante 401
            out, errores = correr(s, sistema, lote)
            err = {e["rel_path"]: e["status_code"] for e in errores}
            for r, o in zip(lote, out):
                w.writerow({"sha256": r["sha256"], "rel_path": r["rel_path"], "exp_tdn1": r["tdn1"],
                            "C_tdn1": o["C_tdn1"], "C_conf": f"{o['C_conf']:.6f}",
                            "error": err.get(r["rel_path"], "")})
            fh.flush()
            print(f"guardados {len(hechos) + i + len(lote)}/{len(cal)}", flush=True)
    print("fin", flush=True)


if __name__ == "__main__":
    main()
