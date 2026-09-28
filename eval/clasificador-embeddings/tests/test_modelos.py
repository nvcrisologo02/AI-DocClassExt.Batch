import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from modelos import Calibrador, ClasificadorKNN, ClasificadorLR


def datos(n=60, semilla=0):
    rng = np.random.default_rng(semilla)
    centros = {"AAAA-01": [5, 0, 0], "AAAA-02": [5, 1, 0], "BBBB-01": [0, 5, 0], "CCCC-01": [0, 0, 5]}
    X, t1, t2 = [], [], []
    for et, c in centros.items():
        for _ in range(n):
            X.append(np.array(c) + rng.normal(0, 0.3, 3))
            t1.append(et[:4])
            t2.append(et)
    X = np.array(X, np.float32)
    return X / np.linalg.norm(X, axis=1, keepdims=True), t1, t2


def test_lr_jerarquico_aprende_familias_y_subtipos():
    X, t1, t2 = datos()
    m = ClasificadorLR().fit(X, t1, t2)
    p = m.predecir(X)
    assert np.mean([a.tdn1 == b for a, b in zip(p, t1)]) > 0.95
    assert all(0 <= a.conf_tdn1 <= 1 for a in p)
    assert {a.tdn2 for a in p if a.tdn1 == "BBBB"} == {"BBBB-01"}  # familia con un solo subtipo


def test_subtipo_con_pocos_ejemplos_no_se_cubre():
    X, t1, t2 = datos()
    t2 = list(t2)
    t2[0:57] = [""] * 57  # AAAA-01 se queda con 3 ejemplos
    p = ClasificadorLR().fit(X, t1, t2, min_ejemplos=5).predecir(X[:3])
    assert all(a.tdn2 in ("", "AAAA-02") for a in p)


def test_knn_misma_interfaz():
    X, t1, t2 = datos()
    p = ClasificadorKNN(k=5).fit(X, t1, t2).predecir(X[:10])
    assert all(a.tdn1 == "AAAA" for a in p)


def test_calibrador_monotono_en_rango():
    c = Calibrador().fit([0.2, 0.4, 0.6, 0.8, 0.9], [0, 0, 1, 1, 1])
    out = c.aplicar([0.1, 0.5, 0.95])
    assert all(0 <= v <= 1 for v in out) and out[0] <= out[1] <= out[2]
