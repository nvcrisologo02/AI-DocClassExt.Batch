"""Enfoques A (regresión logística jerárquica) y B (kNN) sobre embeddings, con calibración
isotónica de la confianza top-1 de TDN1."""
from __future__ import annotations

from collections import Counter
from typing import NamedTuple

import numpy as np
from sklearn.isotonic import IsotonicRegression
from sklearn.linear_model import LogisticRegression


class Prediccion(NamedTuple):
    tdn1: str
    tdn2: str
    conf_tdn1: float


def _subtipos_cubiertos(tdn1, tdn2, min_ejemplos):
    n = Counter(t for t in tdn2 if t)
    return {t for t, c in n.items() if c >= min_ejemplos}


class ClasificadorLR:
    def __init__(self, C: float = 10.0):
        self.C = C

    def fit(self, X, tdn1, tdn2, min_ejemplos: int = 5):
        n1 = Counter(tdn1)
        m = np.array([n1[t] >= min_ejemplos for t in tdn1])
        self.m1 = LogisticRegression(C=self.C, max_iter=3000).fit(X[m], np.array(tdn1)[m])
        cubiertos = _subtipos_cubiertos(tdn1, tdn2, min_ejemplos)
        self.m2 = {}
        for fam in self.m1.classes_:
            idx = [i for i, (a, b) in enumerate(zip(tdn1, tdn2)) if a == fam and b in cubiertos]
            subt = sorted({tdn2[i] for i in idx})
            if len(subt) == 1:
                self.m2[fam] = subt[0]
            elif len(subt) > 1:
                self.m2[fam] = LogisticRegression(C=self.C, max_iter=3000).fit(X[idx], [tdn2[i] for i in idx])
        return self

    def predecir(self, X):
        P = self.m1.predict_proba(X)
        out = []
        for x, fila in zip(X, P):
            j = int(np.argmax(fila))
            fam = self.m1.classes_[j]
            m2 = self.m2.get(fam, "")
            sub = m2 if isinstance(m2, str) else m2.predict(x[None, :])[0]
            out.append(Prediccion(fam, sub, float(fila[j])))
        return out


class ClasificadorKNN:
    def __init__(self, k: int = 15):
        self.k = k

    def fit(self, X, tdn1, tdn2, min_ejemplos: int = 5):
        self.X = X
        self.t1 = np.array(tdn1)
        cub = _subtipos_cubiertos(tdn1, tdn2, min_ejemplos)
        self.t2 = np.array([t if t in cub else "" for t in tdn2])
        return self

    def predecir(self, X):
        S = X @ self.X.T  # vectores normalizados: coseno
        out = []
        for s in S:
            vec = np.argpartition(-s, self.k)[: self.k]
            pesos = Counter()
            for i in vec:
                pesos[self.t1[i]] += max(float(s[i]), 0.0)
            fam, w = pesos.most_common(1)[0]
            total = sum(pesos.values()) or 1.0
            subs = Counter(self.t2[i] for i in vec if self.t1[i] == fam and self.t2[i])
            out.append(Prediccion(str(fam), subs.most_common(1)[0][0] if subs else "", w / total))
        return out


class Calibrador:
    def fit(self, conf, correcto):
        self.iso = IsotonicRegression(y_min=0.0, y_max=1.0, out_of_bounds="clip").fit(
            np.asarray(conf, float), np.asarray(correcto, float))
        return self

    def aplicar(self, conf):
        return self.iso.predict(np.asarray(conf, float))


def calibrar(modelo, X_cal, tdn1_cal) -> Calibrador:
    p = modelo.predecir(X_cal)
    return Calibrador().fit([a.conf_tdn1 for a in p], [a.tdn1 == t for a, t in zip(p, tdn1_cal)])
