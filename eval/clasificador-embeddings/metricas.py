"""Métricas de la prueba: acierto, calibración (ECE), cobertura con acierto mínimo y McNemar."""
from __future__ import annotations

from math import comb

import numpy as np


def acierto(y, p) -> float:
    y, p = np.asarray(y), np.asarray(p)
    return float((y == p).mean()) if len(y) else 0.0


def ece(confianza, correcto, n_bins: int = 10) -> float:
    c, k = np.asarray(confianza, float), np.asarray(correcto, float)
    bins = np.minimum((c * n_bins).astype(int), n_bins - 1)
    total = 0.0
    for b in range(n_bins):
        m = bins == b
        if m.any():
            total += m.sum() / len(c) * abs(k[m].mean() - c[m].mean())
    return float(total)


def umbral_para_acierto(confianza, correcto, objetivo: float = 0.90) -> float:
    c, k = np.asarray(confianza, float), np.asarray(correcto, float)
    orden = np.argsort(-c, kind="stable")
    c, k = c[orden], k[orden]
    mejor = float("inf")
    aciertos = np.cumsum(k) / np.arange(1, len(k) + 1)
    for i in range(len(c)):
        # solo se corta donde cambia la confianza, para que el umbral sea aplicable
        if (i + 1 == len(c) or c[i + 1] < c[i]) and aciertos[i] >= objetivo:
            mejor = float(c[i])
    return mejor


def cobertura_y_acierto(confianza, correcto, t: float):
    c, k = np.asarray(confianza, float), np.asarray(correcto, float)
    m = c >= t
    return (float(m.mean()), float(k[m].mean()) if m.any() else 0.0)


def mcnemar(correcto_a, correcto_b):
    a, b_ = np.asarray(correcto_a, bool), np.asarray(correcto_b, bool)
    b = int((a & ~b_).sum())
    c = int((~a & b_).sum())
    n = b + c
    if n == 0:
        return b, c, 1.0
    p = 2 * sum(comb(n, i) for i in range(min(b, c) + 1)) / 2 ** n
    return b, c, min(1.0, p)
