import base64
import gzip
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from texto import descomprimir, normalizar_sha256, pendientes_y_origen


def test_prefiere_binario():
    assert descomprimir(gzip.compress("hola".encode()), base64.b64encode(gzip.compress(b"viejo")).decode()) == "hola"


def test_cae_a_base64_historico_con_saltos():
    b64 = base64.b64encode(gzip.compress("año".encode())).decode()
    assert descomprimir(None, b64[:4] + "\r\n" + b64[4:]) == "año"


def test_vacio():
    assert descomprimir(None, None) == ""


def test_gzip_corrupto_no_lanza():
    bueno = gzip.compress(b"contenido de prueba largo para que haya cuerpo")
    corrupto = bueno[:10] + b"XYZBASURA" * 5 + bueno[-4:]  # cabecera gzip valida, cuerpo basura
    assert descomprimir(corrupto, None) == ""


def test_normalizar_sha256_pasa_a_minusculas_y_recorta():
    assert normalizar_sha256(" ABC123 ") == "abc123"


def test_normalizar_sha256_admite_bytes():
    assert normalizar_sha256(b"ABC123") == "abc123"


def test_pendientes_son_los_que_no_tienen_texto(monkeypatch):
    monkeypatch.setattr("texto.leer_md", lambda sha: "hola" if sha == "h1" else None)
    inv = [{"sha256": "h1", "rel_path": "a", "paginas": "2"}, {"sha256": "h2", "rel_path": "b", "paginas": "-1"}]
    pend, filas = pendientes_y_origen(inv, {"h1": "bd_dev"})
    assert [p["sha256"] for p in pend] == ["h2"]
    assert filas == [["h1", "bd_dev", 4], ["h2", "sin_texto", 0]]


def test_md_en_cache_sin_origen_no_es_pendiente(monkeypatch):
    monkeypatch.setattr("texto.leer_md", lambda sha: "hola mundo" if sha == "h1" else None)
    inv = [{"sha256": "h1", "rel_path": "a", "paginas": "2"}, {"sha256": "h2", "rel_path": "b", "paginas": "1"}]
    pend, filas = pendientes_y_origen(inv, {})
    assert [p["sha256"] for p in pend] == ["h2"]
    assert filas == [["h1", "cache", 10], ["h2", "sin_texto", 0]]
