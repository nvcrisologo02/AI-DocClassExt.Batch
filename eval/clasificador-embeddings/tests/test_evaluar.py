import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evaluar import hibrido
from modelos import Prediccion


def test_decide_el_modelo_si_supera_el_umbral():
    assert hibrido(Prediccion("NOTS", "NOTS-01", 0.99), 0.97, 0.9, "ESIN", "ESIN-34") == ("NOTS", "NOTS-01", True)


def test_decide_gpt_por_debajo_del_umbral():
    assert hibrido(Prediccion("NOTS", "NOTS-01", 0.5), 0.4, 0.9, "ESIN", "ESIN-34") == ("ESIN", "ESIN-34", False)


def test_subtipo_no_cubierto_toma_el_de_gpt_si_coincide_la_familia():
    assert hibrido(Prediccion("NOTS", "", 0.99), 0.95, 0.9, "NOTS", "NOTS-02") == ("NOTS", "NOTS-02", True)
    assert hibrido(Prediccion("NOTS", "", 0.99), 0.95, 0.9, "ESIN", "ESIN-34") == ("NOTS", "", True)
