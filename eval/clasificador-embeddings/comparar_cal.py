"""Compara A (LR entrenado en 'train') y C (logprobs, salida de logprobs_cal.py) sobre los
mismos documentos de 'cal': acierto, McNemar, ECE bruta, cobertura por umbral, familias
donde más difieren y la misma comparación sin casi duplicados de train.
Solo lee ficheros locales. Uso: python comparar_cal.py"""
from __future__ import annotations

import csv

import numpy as np

from comun import CACHE, EVAL
from embeddings import cargar
from metricas import ece, mcnemar
from modelos import ClasificadorLR


def leer(p):
    return list(csv.DictReader(p.open(encoding="utf-8-sig"), delimiter=";"))


def main() -> None:
    inv = {r["sha256"]: r for r in leer(CACHE / "inventario.csv")}
    c = {r["sha256"]: r for r in leer(EVAL / "runs" / "cal-logprobs-gpt" / "resultados.csv")}
    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}
    tr = [h for h, r in inv.items() if r["particion"] == "train" and h in pos]
    X_tr = X[[pos[h] for h in tr]]
    A = ClasificadorLR().fit(X_tr, [inv[h]["tdn1"] for h in tr], [inv[h]["tdn2"] for h in tr])

    hs = [h for h in c if h in pos]
    X_c = X[[pos[h] for h in hs]]
    y = np.array([inv[h]["tdn1"] for h in hs])
    pa = A.predecir(X_c)
    a_ok = np.array([p.tdn1 for p in pa]) == y
    c_ok = np.array([c[h]["C_tdn1"] for h in hs]) == y
    a_conf = np.array([p.conf_tdn1 for p in pa])
    c_conf = np.array([float(c[h]["C_conf"]) for h in hs])

    print(f"documentos comparados: {len(hs)} (errores C: {sum(1 for h in hs if c[h]['error'])})")
    print(f"acierto TDN1  A={a_ok.mean():.3f}  C={c_ok.mean():.3f}  "
          f"ambos={np.mean(a_ok & c_ok):.3f}  alguno={np.mean(a_ok | c_ok):.3f}")
    b, cc, p = mcnemar(a_ok, c_ok)
    print(f"McNemar A vs C: solo A acierta {b}, solo C acierta {cc}, p={p:.4g}")
    print(f"ECE bruta  A={ece(a_conf, a_ok):.3f}  C={ece(c_conf, c_ok):.3f}")
    for nombre, conf, ok in (("A", a_conf, a_ok), ("C", c_conf, c_ok)):
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
    fmt = [(f, n, f"A {a:.0%}", f"C {x:.0%}") for _, f, n, a, x in dif]
    print("familias (n>=10) con mayor ventaja de C:", fmt[:5])
    print("familias (n>=10) con mayor ventaja de A:", fmt[-5:])

    sim = (X_c @ X_tr.T).max(1)
    for lim in (0.98, 0.95):
        m = sim < lim
        cub = a_conf[m] >= 0.9
        print(f"sin vecino >= {lim} en train: n={m.sum()} A={a_ok[m].mean():.3f} C={c_ok[m].mean():.3f} | "
              f"A conf>=0.9 cobertura {cub.mean():.1%} acierto {a_ok[m][cub].mean():.1%}")


if __name__ == "__main__":
    main()
