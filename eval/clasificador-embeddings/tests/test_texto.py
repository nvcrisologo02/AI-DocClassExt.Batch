import base64
import gzip
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from texto import descomprimir, normalizar_sha256


def test_prefiere_binario():
    assert descomprimir(gzip.compress("hola".encode()), base64.b64encode(gzip.compress(b"viejo")).decode()) == "hola"


def test_cae_a_base64_historico_con_saltos():
    b64 = base64.b64encode(gzip.compress("año".encode())).decode()
    assert descomprimir(None, b64[:4] + "\r\n" + b64[4:]) == "año"


def test_vacio():
    assert descomprimir(None, None) == ""


def test_normalizar_sha256_pasa_a_minusculas_y_recorta():
    assert normalizar_sha256(" ABC123 ") == "abc123"


def test_normalizar_sha256_admite_bytes():
    assert normalizar_sha256(b"ABC123") == "abc123"
