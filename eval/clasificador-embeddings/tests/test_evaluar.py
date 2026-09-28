import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evaluar import hibrido, familias_excluidas, contar_familias_en_golden, percentil_seguro
from modelos import Prediccion


def test_decide_el_modelo_si_supera_el_umbral():
    assert hibrido(Prediccion("NOTS", "NOTS-01", 0.99), 0.97, 0.9, "ESIN", "ESIN-34") == ("NOTS", "NOTS-01", True)


def test_decide_gpt_por_debajo_del_umbral():
    assert hibrido(Prediccion("NOTS", "NOTS-01", 0.5), 0.4, 0.9, "ESIN", "ESIN-34") == ("ESIN", "ESIN-34", False)


def test_subtipo_no_cubierto_toma_el_de_gpt_si_coincide_la_familia():
    assert hibrido(Prediccion("NOTS", "", 0.99), 0.95, 0.9, "NOTS", "NOTS-02") == ("NOTS", "NOTS-02", True)
    assert hibrido(Prediccion("NOTS", "", 0.99), 0.95, 0.9, "ESIN", "ESIN-34") == ("NOTS", "", True)


def test_familias_excluidas_filtra_menores_a_5():
    t1_tr = ["NOTS", "NOTS", "NOTS", "NOTS", "NOTS", "ESIN", "ESIN", "ESIN", "CERA"]
    assert familias_excluidas(t1_tr) == ["CERA", "ESIN"]


def test_familias_excluidas_vacia_si_todas_tienen_5_o_mas():
    t1_tr = ["NOTS"] * 5 + ["ESIN"] * 5
    assert familias_excluidas(t1_tr) == []


def test_contar_familias_en_golden():
    golden = [{"tdn1": "NOTS"}, {"tdn1": "ESIN"}, {"tdn1": "CERA"}, {"tdn1": "NOTS"}]
    assert contar_familias_en_golden(golden, {"CERA"}) == 1
    assert contar_familias_en_golden(golden, {"NOTS", "ESIN"}) == 3
    assert contar_familias_en_golden(golden, set()) == 0


def test_percentil_seguro_con_lista_vacia():
    assert percentil_seguro([], 95) is None


def test_percentil_seguro_con_valores():
    assert percentil_seguro([1.0, 2.0, 3.0, 4.0, 5.0], 95) == 4.8
