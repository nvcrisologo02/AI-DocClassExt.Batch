import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from metricas import acierto, cobertura_y_acierto, ece, mcnemar, umbral_para_acierto


def test_acierto():
    assert acierto(["A", "B", "C", "D"], ["A", "B", "X", "D"]) == 0.75


def test_ece_perfectamente_calibrado_es_cero():
    conf = [0.8] * 10
    corr = [1] * 8 + [0] * 2
    assert math.isclose(ece(conf, corr), 0.0, abs_tol=1e-9)


def test_ece_sobreconfiado():
    assert math.isclose(ece([1.0] * 4, [1, 0, 1, 0]), 0.5, abs_tol=1e-9)


def test_umbral_y_cobertura():
    conf = [0.99, 0.95, 0.9, 0.6, 0.5]
    corr = [1, 1, 1, 0, 1]
    t = umbral_para_acierto(conf, corr, 0.90)
    assert t == 0.9
    assert cobertura_y_acierto(conf, corr, t) == (0.6, 1.0)


def test_umbral_inexistente():
    assert umbral_para_acierto([0.9, 0.8], [0, 0], 0.9) == float("inf")


def test_mcnemar_exacto():
    b, c, p = mcnemar([1] * 10 + [0] * 0, [0] * 10)
    assert (b, c) == (10, 0)
    assert math.isclose(p, 2 * 0.5 ** 10)
    assert mcnemar([1, 0], [1, 0])[2] == 1.0
