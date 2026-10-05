"""Híbrido A (confianza >= umbral) + GPT de producción sobre cal, fuera de línea (AB#100779).

Cruza eval/runs/20260928-215126-gpt-cal/results.csv (AB#100775) con las predicciones de A y
mide, por umbral de confianza TDN1 de A: acierto TDN1 y TDN2 del híbrido y porcentaje de
llamadas GPT ahorradas. Solo lee ficheros locales; no llama a ningún servicio.

Uso: python hibrido_cal.py"""
from __future__ import annotations

import csv

import numpy as np

from comun import CACHE, EVAL
from embeddings import cargar
from metricas import mcnemar
from modelos import ClasificadorLR

RUN = EVAL / "runs" / "20260928-215126-gpt-cal" / "results.csv"


def leer(p):
    return list(csv.DictReader(p.open(encoding="utf-8-sig"), delimiter=";"))


def main() -> None:
    inv = {r["sha256"]: r for r in leer(CACHE / "inventario.csv")}
    por_ruta = {r["rel_path"].lower(): h for h, r in inv.items()}
    gpt, sin_sha = {}, 0
    for r in leer(RUN):
        h = por_ruta.get(r["rel_path"].replace("\\", "/").lower())
        if h is None:
            sin_sha += 1
            continue
        gpt[h] = (r["predicted_tdn1"] or "", r["predicted_tdn2"] or "")
    if sin_sha:
        print(f"aviso: {sin_sha} filas del results.csv no están en el inventario")

    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}
    tr = [h for h, r in inv.items() if r["particion"] == "train" and h in pos]
    A = ClasificadorLR().fit(
        X[[pos[h] for h in tr]], [inv[h]["tdn1"] for h in tr], [inv[h]["tdn2"] for h in tr]
    )

    hs = [h for h in gpt if h in pos]
    y = np.array([inv[h]["tdn1"] for h in hs])
    y2 = np.array([inv[h]["tdn2"] for h in hs])
    pa = A.predecir(X[[pos[h] for h in hs]])
    conf = np.array([p.conf_tdn1 for p in pa])
    a_ok = np.array([p.tdn1 for p in pa]) == y
    a2_ok = np.array([p.tdn2 for p in pa]) == y2
    g_ok = np.array([gpt[h][0] for h in hs]) == y
    g2_ok = np.array([gpt[h][1] for h in hs]) == y2

    print(f"documentos comparados: {len(hs)}")
    print(f"solo A:   TDN1 {a_ok.mean():.1%}  TDN2 {a2_ok.mean():.1%}")
    print(f"solo GPT: TDN1 {g_ok.mean():.1%}  TDN2 {g2_ok.mean():.1%}\n")
    print("umbral | ahorro GPT | TDN1 híbrido | TDN2 híbrido | acierto tramo A | acierto tramo GPT (TDN1)")
    for t in (0.5, 0.6, 0.7, 0.8, 0.85, 0.9, 0.95, 0.99):
        usa_a = conf >= t
        h1_ok = np.where(usa_a, a_ok, g_ok)
        h2_ok = np.where(usa_a, a2_ok, g2_ok)
        tramo_a = a_ok[usa_a].mean() if usa_a.any() else float("nan")
        tramo_g = g_ok[~usa_a].mean() if (~usa_a).any() else float("nan")
        print(f"  {t:.2f} |   {usa_a.mean():5.1%}   |    {h1_ok.mean():.1%}     |    {h2_ok.mean():.1%}     |"
              f"      {tramo_a:5.1%}     |      {tramo_g:5.1%}")

    # significación de los puntos de operación candidatos frente a cada componente
    for t in (0.5, 0.6, 0.9):
        usa_a = conf >= t
        h1_ok = np.where(usa_a, a_ok, g_ok)
        h2_ok = np.where(usa_a, a2_ok, g2_ok)
        for nombre, r1, r2 in (("solo A", a_ok, a2_ok), ("solo GPT", g_ok, g2_ok)):
            b, c, p = mcnemar(h1_ok, r1)
            _, _, p2 = mcnemar(h2_ok, r2)
            print(f"híbrido {t} frente a {nombre}: TDN1 {h1_ok.mean():.3f} vs {r1.mean():.3f} "
                  f"(solo híbrido {b}, solo ref {c}, p={p:.3g}) | TDN2 p={p2:.3g}")

    # familias donde el híbrido 0.9 cambia el resultado frente a solo GPT (n>=10)
    usa_a = conf >= 0.9
    h1_ok = np.where(usa_a, a_ok, g_ok)
    print("\npor familia (n>=10), híbrido 0.9 frente a GPT, ordenado por mejora:")
    dif = []
    for f in sorted(set(y)):
        m = y == f
        if m.sum() >= 10:
            dif.append((h1_ok[m].mean() - g_ok[m].mean(), str(f), int(m.sum()), h1_ok[m].mean(), g_ok[m].mean()))
    for d, f, n, hh, gg in sorted(dif, reverse=True):
        if abs(d) >= 0.005:
            print(f"  {f} n={n:4d}  híbrido {hh:5.1%}  GPT {gg:5.1%}  dif {d:+.1%}")


if __name__ == "__main__":
    main()
