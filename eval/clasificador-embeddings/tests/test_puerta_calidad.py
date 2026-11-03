import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from exportar_modelo import exportar_modelo  # noqa: E402
from modelos import ClasificadorLR  # noqa: E402
from puerta_calidad import evaluar, predecir_artefacto, veredicto  # noqa: E402
from test_exportar_modelo import datos  # noqa: E402


def test_predecir_artefacto_coincide_con_sklearn():
    X, t1, t2 = datos()
    A = ClasificadorLR().fit(X, t1, t2)
    doc = exportar_modelo(A, {"version": "t", "dimensiones": 4})
    p1, p2, conf = predecir_artefacto(doc, X)
    pred = A.predecir(X)
    assert p1 == [p.tdn1 for p in pred] and p2 == [p.tdn2 for p in pred]
    assert np.allclose(conf, [p.conf_tdn1 for p in pred], atol=1e-6)


def test_veredicto():
    assert veredicto(0.872, 0.872, 0.80) == (True, [])
    ok, motivos = veredicto(0.872, 0.871, 0.95)
    assert not ok and "cal" in motivos[0]
    ok, motivos = veredicto(0.872, 0.90, 0.79)
    assert not ok and "hold2" in motivos[0]


def test_evaluar_sobre_datos_de_entrenamiento():
    X, t1, t2 = datos()
    A = ClasificadorLR().fit(X, t1, t2)
    doc = exportar_modelo(A, {"version": "t", "dimensiones": 4})
    r = evaluar(doc, X, t1, t2)
    assert r["n"] == len(X) and 0.9 <= r["tdn1"] <= 1.0 and 0.0 <= r["tdn2"] <= 1.0
    assert set(r) == {"tdn1", "tdn2", "ece", "cobertura_06", "acierto_06", "n"}
