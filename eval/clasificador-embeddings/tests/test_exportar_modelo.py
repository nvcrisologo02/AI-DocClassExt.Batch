import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from exportar_modelo import (  # noqa: E402
    construir_fixture_sintetico,
    exportar_modelo,
    paridad,
    tipologias_cubiertas,
)
from modelos import ClasificadorLR  # noqa: E402


def datos(n=80, semilla=0):
    rng = np.random.default_rng(semilla)
    centros = {
        "AAAA-01": [5, 0, 0, 0], "AAAA-02": [5, 1, 0, 0], "AAAA-03": [5, 0, 1, 0],
        "BBBB-01": [0, 5, 0, 0], "BBBB-02": [0, 5, 1, 0],
        "CCCC-01": [0, 0, 5, 0],
        "DDDD-01": [0, 0, 0, 5],
    }
    X, t1, t2 = [], [], []
    for i in range(n):
        k = list(centros)[i % len(centros)]
        X.append(np.array(centros[k], float) + rng.normal(0, 0.3, 4))
        t1.append(k[:4])
        # DDDD no tiene ningun TDN2 etiquetado y por tanto no tiene modelo TDN2
        t2.append("" if k == "DDDD-01" and i % 14 else k)
    X = np.array(X)
    return X / np.linalg.norm(X, axis=1, keepdims=True), t1, t2


def test_exportar_modelo_reproduce_predict_proba():
    X, t1, t2 = datos()
    A = ClasificadorLR().fit(X, t1, t2)
    doc = exportar_modelo(A, {"version": "test", "dimensiones": 4})
    assert doc["tdn1"]["clases"] == list(A.m1.classes_)
    assert len(doc["tdn1"]["coef"]) == len(A.m1.classes_)
    assert all(len(f) == 4 for f in doc["tdn1"]["coef"])
    # AAAA: tres subtipos -> multiclase; BBBB: dos -> binario (una fila de coef)
    assert len(doc["tdn2"]["AAAA"]["coef"]) == 3
    assert len(doc["tdn2"]["BBBB"]["coef"]) == 1
    assert doc["tdn2"]["CCCC"] == {"constante": "CCCC-01"}
    # Reconstruccion numerica: softmax(X.coef^T + b) == predict_proba
    W = np.array(doc["tdn1"]["coef"]); b = np.array(doc["tdn1"]["intercept"])
    z = X @ W.T + b
    p = np.exp(z - z.max(axis=1, keepdims=True)); p /= p.sum(axis=1, keepdims=True)
    assert np.allclose(p, A.m1.predict_proba(X), atol=1e-9)


def test_tipologias_cubiertas_solo_incluye_pares_predecibles():
    X, t1, t2 = datos()
    A = ClasificadorLR().fit(X, t1, t2)
    cat = [
        {"codigo": "a.01", "tdn1": "aaaa", "tdn2": "aaaa-01"},
        {"codigo": "a.09", "tdn1": "AAAA", "tdn2": "AAAA-09"},   # subtipo no entrenado
        {"codigo": "c.01", "tdn1": "CCCC", "tdn2": "CCCC-01"},   # constante
        {"codigo": "z.01", "tdn1": "ZZZZ", "tdn2": "ZZZZ-01"},   # familia ausente
    ]
    out = tipologias_cubiertas(A, cat)
    assert [t["codigo"] for t in out] == ["a.01", "c.01"]
    assert out[0] == {"codigo": "a.01", "tdn1": "AAAA", "tdn2": "AAAA-01"}


def test_paridad_lleva_vector_y_distribuciones():
    X, t1, t2 = datos()
    A = ClasificadorLR().fit(X, t1, t2)
    par = paridad(A, X, [f"sha{i}" for i in range(len(X))], n=3)
    assert len(par["casos"]) == 3
    caso = par["casos"][0]
    assert len(caso["vector"]) == 4
    assert abs(sum(caso["probTdn1"].values()) - 1) < 1e-9
    assert abs(sum(caso["probTdn2"].values()) - 1) < 1e-9
    assert caso["tdn2"] == A.predecir(X[:1])[0].tdn2


def test_fixture_sintetico_es_json_valido(tmp_path):
    construir_fixture_sintetico(tmp_path)
    modelo = json.loads((tmp_path / "clasificador-embeddings-fixture.json").read_text(encoding="utf-8"))
    par = json.loads((tmp_path / "paridad-fixture.json").read_text(encoding="utf-8"))
    assert modelo["manifiesto"]["dimensiones"] == 8
    assert modelo["manifiesto"]["calibrado"] is False
    tipos = {type(v.get("constante", None)) for v in modelo["tdn2"].values()}
    assert str in tipos  # al menos una familia constante
    assert any("coef" in v and len(v["coef"]) == 1 for v in modelo["tdn2"].values())  # una binaria
    assert any("coef" in v and len(v["coef"]) >= 3 for v in modelo["tdn2"].values())  # una multiclase
    assert len(par["casos"]) == 7
    assert par["casos"][-1]["tdn1"] == "DDDD" and par["casos"][-1]["tdn2"] == ""
