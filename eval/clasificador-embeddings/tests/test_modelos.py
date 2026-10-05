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


def test_subtipo_no_cubierto_predice_vacio():
    """Familia con un subtipo cubierto (>=5) y otro no cubierto (3):
    puntos del no cubierto deben predecir "" en LR y kNN."""
    rng = np.random.default_rng(1)
    # DDDD-A: 6 ejemplos (cubierto)
    # DDDD-B: 3 ejemplos (no cubierto)
    # EEEE-X: 10 ejemplos (para tener 2 familias)
    X_a = np.array([np.array([5, 0, 0]) + rng.normal(0, 0.2, 3) for _ in range(6)], dtype=np.float32)
    X_b = np.array([np.array([0, 5, 0]) + rng.normal(0, 0.2, 3) for _ in range(3)], dtype=np.float32)
    X_e = np.array([np.array([0, 0, 5]) + rng.normal(0, 0.2, 3) for _ in range(10)], dtype=np.float32)
    X = np.vstack([X_a, X_b, X_e])
    X = X / np.linalg.norm(X, axis=1, keepdims=True)
    t1 = ["DDDD"] * 9 + ["EEEE"] * 10
    t2 = ["DDDD-A"] * 6 + ["DDDD-B"] * 3 + ["EEEE-X"] * 10

    # LR: puntos de DDDD-B predicen ""
    m_lr = ClasificadorLR().fit(X, t1, t2, min_ejemplos=5)
    p_lr = m_lr.predecir(X[6:9])  # los 3 puntos de DDDD-B
    assert all(a.tdn1 == "DDDD" and a.tdn2 == "" for a in p_lr), f"LR falló: {p_lr}"

    # kNN: idem
    m_knn = ClasificadorKNN(k=5).fit(X, t1, t2, min_ejemplos=5)
    p_knn = m_knn.predecir(X[6:9])
    assert all(a.tdn1 == "DDDD" and a.tdn2 == "" for a in p_knn), f"kNN falló: {p_knn}"


def test_tdn2_vacio_en_entrada_no_entra():
    """Documentos con tdn2 vacio en la entrada no entran en el modelo TDN2."""
    rng = np.random.default_rng(2)
    # EEEE-X: 5 ejemplos (cubierto)
    # Empty: 3 ejemplos sin etiqueta (entrada vacia)
    # FFFF: 10 ejemplos (para tener 2 familias)
    X_x = np.array([np.array([5, 0, 0]) + rng.normal(0, 0.2, 3) for _ in range(5)], dtype=np.float32)
    X_empty = np.array([np.array([5, 1, 0]) + rng.normal(0, 0.2, 3) for _ in range(3)], dtype=np.float32)
    X_f = np.array([np.array([0, 5, 0]) + rng.normal(0, 0.2, 3) for _ in range(10)], dtype=np.float32)
    X = np.vstack([X_x, X_empty, X_f])
    X = X / np.linalg.norm(X, axis=1, keepdims=True)
    t1 = ["EEEE"] * 8 + ["FFFF"] * 10
    t2 = ["EEEE-X"] * 5 + [""] * 3 + ["FFFF-X"] * 10  # 3 documentos sin etiqueta

    m = ClasificadorLR().fit(X, t1, t2, min_ejemplos=5)
    p = m.predecir(X)
    # Todos predicen sus familias correctas
    assert all(p[i].tdn1 == t1[i] for i in range(len(t1)))


def test_argpartition_con_k_mayor_que_ejemplos():
    """argpartition no falla si k >= numero de ejemplos de entrenamiento."""
    X = np.array([[1, 0, 0], [0, 1, 0], [0, 0, 1]], dtype=np.float32)
    X = X / np.linalg.norm(X, axis=1, keepdims=True)
    t1 = ["A", "B", "C"]
    t2 = ["A-01", "B-01", "C-01"]

    # kNN con k=15 pero solo 3 ejemplos de entrenamiento
    m = ClasificadorKNN(k=15).fit(X, t1, t2)
    p = m.predecir(X[:1])  # predecir sobre el primer punto
    assert len(p) == 1
    assert p[0].tdn1 == "A"
