import hashlib
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inventario import huella, hay_que_releer, parsear_etiqueta, particionar


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


def test_huella_de_bytes_no_pdf():
    datos = b"esto no es un pdf, son bytes cualquiera"
    sha, paginas = huella(datos)
    assert sha == hashlib.sha256(datos).hexdigest()
    assert paginas == -1


def test_huella_calcula_paginas_de_pdf_valido(tmp_path):
    from pypdf import PdfWriter

    writer = PdfWriter()
    writer.add_blank_page(width=72, height=72)
    writer.add_blank_page(width=72, height=72)
    destino = tmp_path / "minimo.pdf"
    with destino.open("wb") as f:
        writer.write(f)
    datos = destino.read_bytes()
    sha, paginas = huella(datos)
    assert sha == hashlib.sha256(datos).hexdigest()
    assert paginas == 2


def test_hay_que_releer_fichero_no_visto():
    checkpoint = {}
    assert hay_que_releer(checkpoint, "AAAA/f.pdf", 123, 456) is True


def test_hay_que_releer_fichero_igual_no_se_relee():
    checkpoint = {"AAAA/f.pdf": {"tamano": 123, "mtime_ns": 456, "sha256": "x", "paginas": 1}}
    assert hay_que_releer(checkpoint, "AAAA/f.pdf", 123, 456) is False


def test_hay_que_releer_si_cambia_tamano_o_mtime():
    checkpoint = {"AAAA/f.pdf": {"tamano": 123, "mtime_ns": 456, "sha256": "x", "paginas": 1}}
    assert hay_que_releer(checkpoint, "AAAA/f.pdf", 999, 456) is True
    assert hay_que_releer(checkpoint, "AAAA/f.pdf", 123, 999) is True
