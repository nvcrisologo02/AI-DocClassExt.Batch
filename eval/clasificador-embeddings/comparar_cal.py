"""Compara A (LR entrenado en 'train') con un GPT sobre los mismos documentos de 'cal': acierto,
McNemar, ECE bruta, cobertura por umbral, familias donde más difieren y la misma comparación
sin casi duplicados de train.

El GPT puede ser C (prompt compacto con logprobs, salida de logprobs_cal.py, por defecto) o el
GPT de producción medido con la consola de evaluación sobre eval/cal.csv (--gpt <results.csv>,
AB#100775). Solo lee ficheros locales.

Uso: python comparar_cal.py [--gpt eval/runs/<run>/results.csv]"""
from __future__ import annotations

import argparse
import csv
from pathlib import Path

import numpy as np

from comun import CACHE, EVAL
from embeddings import cargar
from metricas import ece, mcnemar
from modelos import ClasificadorLR


def leer(p):
    return list(csv.DictReader(p.open(encoding="utf-8-sig"), delimiter=";"))


def cargar_c_logprobs(inv):
    """C: sha -> (tdn1, tdn2, conf, error). Sin TDN2 (el prompt compacto solo predice familia)."""
    c = {}
    for r in leer(EVAL / "runs" / "cal-logprobs-gpt" / "resultados.csv"):
        c[r["sha256"]] = (r["C_tdn1"], None, float(r["C_conf"]), bool(r["error"]))
    return "C (prompt compacto)", c


def cargar_gpt_harness(inv, ruta: Path):
    """GPT de producción: results.csv del harness, cruzado por rel_path con el inventario."""
    por_ruta = {r["rel_path"].lower(): h for h, r in inv.items()}
    c, sin_sha = {}, 0
    for r in leer(ruta):
        h = por_ruta.get(r["rel_path"].replace("\\", "/").lower())
        if h is None:
            sin_sha += 1
            continue
        ok = r["estado"].upper() == "OK" and r["rate_limit"].lower() != "true" and r["predicted_tdn1"]
        conf = float(r["confianza"]) if r["confianza"] else 0.0
        c[h] = (r["predicted_tdn1"] or "", r["predicted_tdn2"] or None, conf, not ok)
    if sin_sha:
        print(f"aviso: {sin_sha} filas del results.csv no están en el inventario")
    return f"GPT producción ({ruta.parent.name})", c


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--gpt", help="results.csv del harness con el GPT de producción sobre cal")
    args = ap.parse_args()

    inv = {r["sha256"]: r for r in leer(CACHE / "inventario.csv")}
    nombre_c, c = cargar_gpt_harness(inv, Path(args.gpt)) if args.gpt else cargar_c_logprobs(inv)
    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}
    tr = [h for h, r in inv.items() if r["particion"] == "train" and h in pos]
    X_tr = X[[pos[h] for h in tr]]
    A = ClasificadorLR().fit(X_tr, [inv[h]["tdn1"] for h in tr], [inv[h]["tdn2"] for h in tr])

    hs = [h for h in c if h in pos]
    X_c = X[[pos[h] for h in hs]]
    y = np.array([inv[h]["tdn1"] for h in hs])
    y2 = np.array([inv[h]["tdn2"] for h in hs])
    pa = A.predecir(X_c)
    a_ok = np.array([p.tdn1 for p in pa]) == y
    c_ok = np.array([c[h][0] for h in hs]) == y
    a_conf = np.array([p.conf_tdn1 for p in pa])
    c_conf = np.array([c[h][2] for h in hs])
    errores = sum(1 for h in hs if c[h][3])

    print(f"A frente a {nombre_c}")
    print(f"documentos comparados: {len(hs)} (sin respuesta válida del GPT: {errores}, cuentan como fallo)")
    print(f"acierto TDN1  A={a_ok.mean():.3f}  GPT={c_ok.mean():.3f}  "
          f"ambos={np.mean(a_ok & c_ok):.3f}  alguno={np.mean(a_ok | c_ok):.3f}")
    b, cc, p = mcnemar(a_ok, c_ok)
    print(f"McNemar A vs GPT: solo A acierta {b}, solo GPT acierta {cc}, p={p:.4g}")
    if any(c[h][1] is not None for h in hs):
        a2_ok = np.array([p.tdn2 for p in pa]) == y2
        c2_ok = np.array([c[h][1] or "" for h in hs]) == y2
        b2, cc2, p2 = mcnemar(a2_ok, c2_ok)
        print(f"acierto TDN2  A={a2_ok.mean():.3f}  GPT={c2_ok.mean():.3f}  "
              f"| McNemar solo A {b2}, solo GPT {cc2}, p={p2:.4g}")
    print(f"ECE bruta  A={ece(a_conf, a_ok):.3f}  GPT={ece(c_conf, c_ok):.3f}")
    for nombre, conf, ok in (("A", a_conf, a_ok), ("GPT", c_conf, c_ok)):
        for t in (0.5, 0.7, 0.9, 0.99):
            m = conf >= t
            acc = ok[m].mean() if m.any() else float("nan")
            print(f"  {nombre} conf>={t}: cobertura {m.mean():.1%}, acierto {acc:.1%}")

    dif = []
    for f in sorted(set(y)):
        m = y == f
        if m.sum() >= 10:
            dif.append((a_ok[m].mean() - c_ok[m].mean(), str(f), int(m.sum()), a_ok[m].mean(), c_ok[m].mean()))
    dif.sort()
    print("por familia (n>=10), ordenado por ventaja de GPT -> ventaja de A:")
    for _, f, n, a, x in dif:
        print(f"  {f} n={n:4d}  A {a:5.1%}  GPT {x:5.1%}  dif {a - x:+.1%}")

    sim = (X_c @ X_tr.T).max(1)
    for lim in (0.98, 0.95):
        m = sim < lim
        cub = a_conf[m] >= 0.9
        print(f"sin vecino >= {lim} en train: n={m.sum()} A={a_ok[m].mean():.3f} GPT={c_ok[m].mean():.3f} | "
              f"A conf>=0.9 cobertura {cub.mean():.1%} acierto {a_ok[m][cub].mean():.1%}")


if __name__ == "__main__":
    main()
