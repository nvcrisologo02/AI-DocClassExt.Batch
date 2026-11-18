import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from exportar_modelo import exportar_modelo  # noqa: E402
from modelos import ClasificadorLR  # noqa: E402
from puerta_calidad import evaluar, particiones_cal_ampliada, predecir_artefacto, veredicto  # noqa: E402
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
    ok, motivos = veredicto(0.872, 0.866, 0.95)
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


def test_veredicto_con_holdout_propio_y_sin_filas():
    assert veredicto(0.874, 0.874, 0.80, holdout="hold3") == (True, [])
    ok, motivos = veredicto(0.874, 0.90, 0.79, holdout="hold3")
    assert not ok and motivos[0].startswith("hold3")
    ok, motivos = veredicto(0.874, 0.90, 0.0, n_hold=0, holdout="hold3")
    assert not ok and "sin documentos" in motivos[0]


def test_veredicto_tolera_caida_de_cal_dentro_del_margen():
    # 2026-11-05: v3 87,1 % frente a v2 87,4 %; el margen por defecto es 0,5 pp
    assert veredicto(0.874, 0.871, 0.95) == (True, [])
    ok, motivos = veredicto(0.874, 0.868, 0.95)
    assert not ok and motivos[0].startswith("cal") and "0.5%" in motivos[0]
    ok, motivos = veredicto(0.874, 0.871, 0.95, tolerancia_cal=0.0)
    assert not ok and motivos[0].startswith("cal")


def test_particiones_cal_ampliada_solo_holdouts_anteriores_al_evaluado():
    # Solo entran los holdouts de cortes ANTERIORES (hold<k> con k < n): el evaluado y los posteriores quedan
    # fuera, porque el modelo anterior no vio esas clases y meterlos haría pasar cualquier versión.
    inv = {f"h{i}": {"sha256": f"h{i}", "particion": p} for i, p in enumerate(
        ["cal", "hold2", "hold3", "hold4", "train", "golden", "hold4", "hold10"])}
    assert particiones_cal_ampliada(inv, "hold4") == ["cal", "hold2", "hold3"]
    assert particiones_cal_ampliada(inv, "hold3") == ["cal", "hold2"]
    assert particiones_cal_ampliada(inv, "hold2") == ["cal"]
    assert particiones_cal_ampliada(inv, "hold10") == ["cal", "hold2", "hold3", "hold4"]
    assert particiones_cal_ampliada({"a": {"sha256": "a", "particion": "cal"}}, "hold2") == ["cal"]


def test_particiones_cal_ampliada_rechaza_holdout_sin_numero():
    import pytest
    with pytest.raises(ValueError, match="hold<n>"):
        particiones_cal_ampliada({"a": {"sha256": "a", "particion": "cal"}}, "holdout")


def test_veredicto_nombra_la_condicion_de_cal():
    ok, motivos = veredicto(0.880, 0.870, 0.90, holdout="hold4", etiqueta_cal="cal_ampliada")
    assert not ok and motivos[0].startswith("cal_ampliada:")
    ok, motivos = veredicto(0.880, 0.870, 0.90, holdout="hold4")
    assert not ok and motivos[0].startswith("cal:")
