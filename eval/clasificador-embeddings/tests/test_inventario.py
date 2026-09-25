import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inventario import parsear_etiqueta, particionar


def test_etiqueta_completa():
    assert parsear_etiqueta("CEDU", "CEDU-01--OP-99-SCXX-00_ATINF00001579_106149101.pdf") == ("CEDU", "CEDU-01", "ok")


def test_sin_tdn2_usa_carpeta():
    assert parsear_etiqueta("BORR", "BORRADOR--OP-99-SCXX-00_X_1.pdf") == ("BORR", None, "sin_tdn2")


def test_carpeta_invalida():
    assert parsear_etiqueta("NO S", "loquesea.pdf") == (None, None, "carpeta_invalida")


def test_prefijo_distinto_de_carpeta_es_conflicto():
    assert parsear_etiqueta("NOTS", "ESIN-34--OP-99_X_1.pdf") == (None, None, "conflicto_carpeta")


def test_particion_respeta_golden_y_estratifica():
    filas = [{"sha256": f"h{i}", "tdn1": "AAAA" if i % 2 else "BBBB", "golden": False} for i in range(100)]
    filas[0]["golden"] = True
    out = particionar(filas, semilla=1, frac_cal=0.2)
    assert out[0]["particion"] == "golden"
    cal = [f for f in out if f["particion"] == "cal"]
    assert 15 <= len(cal) <= 25
    assert {f["tdn1"] for f in cal} == {"AAAA", "BBBB"}
    assert particionar(filas, semilla=1) == out  # determinista
