import csv
import sys
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import di_layout  # noqa: E402  (sys.path se ajusta arriba)
import texto  # noqa: E402
from di_layout import paginas_facturables, rango_paginas


def test_recorte():
    assert paginas_facturables(12, 5) == 5
    assert paginas_facturables(3, 5) == 3


def test_paginas_desconocidas_cuentan_el_maximo():
    assert paginas_facturables(-1, 5) == 5


def test_rango_paginas_recorte():
    assert rango_paginas(2, 5) == "1-2"
    assert rango_paginas(12, 5) == "1-5"


def test_rango_paginas_desconocidas():
    assert rango_paginas(-1, 5) == "1-5"


def test_ya_en_cache(tmp_path, monkeypatch):
    monkeypatch.setattr(texto, "CACHE", tmp_path)
    sha = "c" * 64
    assert di_layout.ya_en_cache(sha) is False
    texto.guardar_md(sha, "contenido de prueba")
    assert di_layout.ya_en_cache(sha) is True


def test_escribir_origen_csv_valido(tmp_path):
    ruta = tmp_path / "texto_origen.csv"
    filas = [
        {"sha256": "a" * 64, "origen_texto": "di_dev", "caracteres": "123"},
        {"sha256": "b" * 64, "origen_texto": "sin_texto", "caracteres": "0"},
    ]
    di_layout.escribir_origen(ruta, filas)
    leidas = list(csv.DictReader(ruta.open(encoding="utf-8-sig"), delimiter=";"))
    assert leidas == filas
    assert not list(ruta.parent.glob("*.tmp"))


def test_pendiente_en_cache_no_llama_a_di(tmp_path, monkeypatch):
    cache = tmp_path / "cache"
    corpus = tmp_path / "corpus"
    (cache / "md").mkdir(parents=True)
    corpus.mkdir()
    monkeypatch.setattr(texto, "CACHE", cache)
    monkeypatch.setattr(di_layout, "CACHE", cache)

    sha = "d" * 64
    contenido = "markdown ya en caché"
    texto.guardar_md(sha, contenido)

    (cache / "pendientes_di.csv").write_text(
        "sha256;rel_path;paginas\n" f"{sha};doc.pdf;3\n", encoding="utf-8-sig"
    )
    (cache / "texto_origen.csv").write_text(
        "sha256;origen_texto;caracteres\n" f"{sha};sin_texto;0\n", encoding="utf-8-sig"
    )

    llamadas = []

    def analizar_falso(*a, **k):
        llamadas.append(a)
        raise AssertionError("no debería llamarse a DI para un pendiente ya en caché")

    monkeypatch.setattr(di_layout, "analizar", analizar_falso)
    monkeypatch.setattr(di_layout, "sesion_http", lambda: object())
    monkeypatch.setattr(sys, "argv", ["di_layout.py", "--lanzar", "--corpus", str(corpus)])

    di_layout.main()

    assert llamadas == []
    filas = list(csv.DictReader((cache / "texto_origen.csv").open(encoding="utf-8-sig"), delimiter=";"))
    assert filas[0]["origen_texto"] == "di_dev"
    assert filas[0]["caracteres"] == str(len(contenido))


def test_pendiente_ya_marcado_di_dev_no_llama_a_di(tmp_path, monkeypatch, capsys):
    """Reanudación: un checkpoint anterior ya dejó la fila en di_dev y el .md.gz sigue
    en caché; --lanzar no debe volver a facturar DI por él (pendientes_di.csv es
    estático entre invocaciones, así que esta fila sigue apareciendo como pendiente)."""
    cache = tmp_path / "cache"
    corpus = tmp_path / "corpus"
    (cache / "md").mkdir(parents=True)
    corpus.mkdir()
    monkeypatch.setattr(texto, "CACHE", cache)
    monkeypatch.setattr(di_layout, "CACHE", cache)

    sha = "e" * 64
    contenido = "markdown de un checkpoint anterior"
    texto.guardar_md(sha, contenido)

    (cache / "pendientes_di.csv").write_text(
        "sha256;rel_path;paginas\n" f"{sha};doc.pdf;3\n", encoding="utf-8-sig"
    )
    (cache / "texto_origen.csv").write_text(
        "sha256;origen_texto;caracteres\n" f"{sha};di_dev;{len(contenido)}\n", encoding="utf-8-sig"
    )

    llamadas = []

    def analizar_falso(*a, **k):
        llamadas.append(a)
        raise AssertionError("no debería llamarse a DI para un pendiente ya marcado di_dev")

    monkeypatch.setattr(di_layout, "analizar", analizar_falso)
    monkeypatch.setattr(di_layout, "sesion_http", lambda: object())
    monkeypatch.setattr(sys, "argv", ["di_layout.py", "--lanzar", "--corpus", str(corpus)])

    di_layout.main()

    assert llamadas == []
    salida = capsys.readouterr().out
    assert "DI ok 0 | reutilizados 1 | vacíos 0 | errores 0" in salida
    filas = list(csv.DictReader((cache / "texto_origen.csv").open(encoding="utf-8-sig"), delimiter=";"))
    assert filas[0]["origen_texto"] == "di_dev"
    assert filas[0]["caracteres"] == str(len(contenido))


def test_lanzar_corpus_inexistente_sale_con_codigo_2(tmp_path, monkeypatch, capsys):
    """--lanzar con --corpus inexistente debe salir con código 2 sin crear sesion_http ni escribir."""
    cache = tmp_path / "cache"
    cache.mkdir()
    monkeypatch.setattr(texto, "CACHE", cache)
    monkeypatch.setattr(di_layout, "CACHE", cache)

    corpus_inexistente = tmp_path / "corpus_fantasma"
    sha = "a" * 64
    (cache / "pendientes_di.csv").write_text(
        f"sha256;rel_path;paginas\n{sha};doc.pdf;3\n", encoding="utf-8-sig"
    )
    (cache / "texto_origen.csv").write_text(
        f"sha256;origen_texto;caracteres\n{sha};sin_texto;0\n", encoding="utf-8-sig"
    )

    llamadas_sesion = []

    def sesion_http_falso():
        llamadas_sesion.append("llamada")
        raise AssertionError("no debería llamarse a sesion_http si corpus no es accesible")

    monkeypatch.setattr(di_layout, "sesion_http", sesion_http_falso)
    monkeypatch.setattr(sys, "argv", ["di_layout.py", "--lanzar", "--corpus", str(corpus_inexistente)])

    try:
        di_layout.main()
        assert False, "debería lanzar SystemExit"
    except SystemExit as e:
        assert e.code == 2, f"expected code 2, got {e.code}"

    assert llamadas_sesion == [], "sesion_http no debería haber sido llamada"
    salida = capsys.readouterr().out
    assert f"corpus no accesible: {corpus_inexistente}" in salida


class FakeResponse:
    """Doble de requests.Response: sin red, sin cabeceras de autenticación reales."""

    def __init__(self, status_code=200, headers=None, json_data=None, text=""):
        self.status_code = status_code
        self.headers = headers or {}
        self._json_data = json_data
        self.text = text

    def raise_for_status(self):
        if self.status_code >= 400:
            raise requests.HTTPError(f"{self.status_code} error", response=self)

    def json(self):
        return self._json_data


class FakeSession:
    """Doble de requests.Session: sirve respuestas de post()/get() en orden, sin red."""

    def __init__(self, respuestas_post=(), respuestas_get=()):
        self._respuestas_post = list(respuestas_post)
        self._respuestas_get = list(respuestas_get)
        self.llamadas_post = 0
        self.llamadas_get = 0

    def post(self, url, json=None, timeout=None):
        self.llamadas_post += 1
        return self._respuestas_post.pop(0)

    def get(self, url, timeout=None):
        self.llamadas_get += 1
        return self._respuestas_get.pop(0)


def test_401_crea_sesion_nueva_y_el_documento_acaba_ok(monkeypatch):
    """(a) un 401 seguido de éxito crea sesión nueva y el documento acaba ok."""
    s1 = FakeSession(respuestas_post=[FakeResponse(status_code=401)])

    resp_post_ok = FakeResponse(status_code=200, headers={"Operation-Location": "https://op/1"})
    resp_get_ok = FakeResponse(
        status_code=200, json_data={"status": "succeeded", "analyzeResult": {"content": "hola mundo"}}
    )
    s2 = FakeSession(respuestas_post=[resp_post_ok], respuestas_get=[resp_get_ok])

    monkeypatch.setattr(di_layout, "sesion_http", lambda: s2)
    monkeypatch.setattr(di_layout.time, "sleep", lambda segundos: None)

    contenido, s_final = di_layout.analizar_con_reintentos(s1, b"pdf", "1-1")

    assert contenido == "hola mundo"
    assert s_final is s2
    assert s1.llamadas_post == 1
    assert s2.llamadas_post == 1
    assert s2.llamadas_get == 1


def test_429_reintenta_sin_dormir_de_verdad(monkeypatch):
    """(b) un 429 seguido de éxito reintenta sin dormir de verdad."""
    resp_429 = FakeResponse(status_code=429)
    resp_post_ok = FakeResponse(status_code=200, headers={"Operation-Location": "https://op/2"})
    resp_get_ok = FakeResponse(
        status_code=200, json_data={"status": "succeeded", "analyzeResult": {"content": "texto"}}
    )
    s = FakeSession(respuestas_post=[resp_429, resp_post_ok], respuestas_get=[resp_get_ok])

    esperas = []
    monkeypatch.setattr(di_layout.time, "sleep", lambda segundos: None)

    contenido, s_final = di_layout.analizar_con_reintentos(s, b"pdf", "1-1", dormir=esperas.append)

    assert contenido == "texto"
    assert s_final is s
    assert esperas == [5]  # primera espera de ESPERAS_TRANSITORIAS, sin Retry-After
    assert s.llamadas_post == 2


def test_401_persistente_falla_tras_un_reintento_sin_bucle_infinito(monkeypatch):
    """(c) un 401 persistente acaba en error tras 1 reintento, sin bucle infinito."""
    s1 = FakeSession(respuestas_post=[FakeResponse(status_code=401)])
    s2 = FakeSession(respuestas_post=[FakeResponse(status_code=401)])

    monkeypatch.setattr(di_layout, "sesion_http", lambda: s2)

    with pytest.raises(di_layout.Autenticacion401):
        di_layout.analizar_con_reintentos(s1, b"pdf", "1-1")

    assert s1.llamadas_post == 1
    assert s2.llamadas_post == 1  # exactamente 1 reintento, no más


def test_sin_operation_location_da_runtimeerror_con_codigo(monkeypatch):
    """(d) una respuesta sin Operation-Location da un RuntimeError con el código."""
    resp = FakeResponse(status_code=200, headers={}, text="cuerpo sin operation location de prueba")
    s = FakeSession(respuestas_post=[resp])

    with pytest.raises(RuntimeError) as exc_info:
        di_layout.analizar(s, b"pdf", "1-1")

    assert isinstance(exc_info.value, di_layout.ErrorDI)
    assert exc_info.value.codigo == 200
    assert s.llamadas_get == 0  # no debe sondear si no hay Operation-Location


def test_transitorio_agota_reintentos_y_propaga_sin_bucle_infinito(monkeypatch):
    """429 persistente: se reintenta como mucho 3 veces (4 intentos totales) y luego se propaga."""
    respuestas = [FakeResponse(status_code=429) for _ in range(4)]
    s = FakeSession(respuestas_post=respuestas)

    esperas = []
    with pytest.raises(requests.HTTPError):
        di_layout.analizar_con_reintentos(s, b"pdf", "1-1", dormir=esperas.append)

    assert s.llamadas_post == 4  # intento inicial + 3 reintentos
    assert esperas == [5, 15, 45]


def test_retry_after_numerico_sustituye_la_espera_por_defecto(monkeypatch):
    resp_429 = FakeResponse(status_code=429, headers={"Retry-After": "2"})
    resp_post_ok = FakeResponse(status_code=200, headers={"Operation-Location": "https://op/3"})
    resp_get_ok = FakeResponse(
        status_code=200, json_data={"status": "succeeded", "analyzeResult": {"content": "ok"}}
    )
    s = FakeSession(respuestas_post=[resp_429, resp_post_ok], respuestas_get=[resp_get_ok])

    esperas = []
    monkeypatch.setattr(di_layout.time, "sleep", lambda segundos: None)
    di_layout.analizar_con_reintentos(s, b"pdf", "1-1", dormir=esperas.append)

    assert esperas == [2.0]


def test_codigo_http_en_linea_de_error_de_main(tmp_path, monkeypatch, capsys):
    """Requisito 5: la línea de error del log trae rel_path, tipo de excepción y código HTTP."""
    cache = tmp_path / "cache"
    corpus = tmp_path / "corpus"
    (cache / "md").mkdir(parents=True)
    corpus.mkdir()
    (corpus / "doc.pdf").write_bytes(b"contenido de prueba")
    monkeypatch.setattr(texto, "CACHE", cache)
    monkeypatch.setattr(di_layout, "CACHE", cache)

    sha = "f" * 64
    (cache / "pendientes_di.csv").write_text(
        f"sha256;rel_path;paginas\n{sha};doc.pdf;3\n", encoding="utf-8-sig"
    )
    (cache / "texto_origen.csv").write_text(
        f"sha256;origen_texto;caracteres\n{sha};sin_texto;0\n", encoding="utf-8-sig"
    )

    s_inicial = FakeSession(respuestas_post=[FakeResponse(status_code=401)])
    s_reintento = FakeSession(respuestas_post=[FakeResponse(status_code=401)])
    sesiones = iter([s_inicial, s_reintento])

    monkeypatch.setattr(di_layout, "sesion_http", lambda: next(sesiones))
    monkeypatch.setattr(sys, "argv", ["di_layout.py", "--lanzar", "--corpus", str(corpus)])

    di_layout.main()

    salida = capsys.readouterr().out
    assert "error doc.pdf Autenticacion401 401" in salida


def test_error_sin_codigo_http_usa_guion(tmp_path, monkeypatch, capsys):
    """ConnectionError persistente: sin código HTTP, la línea de error trae '-'."""
    cache = tmp_path / "cache"
    corpus = tmp_path / "corpus"
    (cache / "md").mkdir(parents=True)
    corpus.mkdir()
    (corpus / "doc.pdf").write_bytes(b"contenido de prueba")
    monkeypatch.setattr(texto, "CACHE", cache)
    monkeypatch.setattr(di_layout, "CACHE", cache)

    sha = "9" * 64
    (cache / "pendientes_di.csv").write_text(
        f"sha256;rel_path;paginas\n{sha};doc.pdf;3\n", encoding="utf-8-sig"
    )
    (cache / "texto_origen.csv").write_text(
        f"sha256;origen_texto;caracteres\n{sha};sin_texto;0\n", encoding="utf-8-sig"
    )

    def analizar_con_reintentos_falso(s, pdf_bytes, rango, dormir=None):
        raise requests.ConnectionError("sin red")

    monkeypatch.setattr(di_layout, "sesion_http", lambda: FakeSession())
    monkeypatch.setattr(di_layout, "analizar_con_reintentos", analizar_con_reintentos_falso)
    monkeypatch.setattr(sys, "argv", ["di_layout.py", "--lanzar", "--corpus", str(corpus)])

    di_layout.main()

    salida = capsys.readouterr().out
    assert "error doc.pdf ConnectionError -" in salida


def test_renovacion_preventiva_antes_de_cada_documento(tmp_path, monkeypatch, capsys):
    """Requisito 3: la renovación se comprueba antes de cada documento, con intervalo de 20 min."""
    cache = tmp_path / "cache"
    corpus = tmp_path / "corpus"
    (cache / "md").mkdir(parents=True)
    corpus.mkdir()
    (corpus / "doc1.pdf").write_bytes(b"contenido 1")
    (corpus / "doc2.pdf").write_bytes(b"contenido 2")
    monkeypatch.setattr(texto, "CACHE", cache)
    monkeypatch.setattr(di_layout, "CACHE", cache)

    sha1, sha2 = "1" * 64, "2" * 64
    (cache / "pendientes_di.csv").write_text(
        f"sha256;rel_path;paginas\n{sha1};doc1.pdf;3\n{sha2};doc2.pdf;3\n", encoding="utf-8-sig"
    )
    (cache / "texto_origen.csv").write_text(
        f"sha256;origen_texto;caracteres\n{sha1};sin_texto;0\n{sha2};sin_texto;0\n",
        encoding="utf-8-sig",
    )

    relojes = iter([0.0, 0.0, 20 * 60 + 1])  # inicial, antes de doc1 (sin pasar), antes de doc2 (pasa el intervalo)
    monkeypatch.setattr(di_layout.time, "monotonic", lambda: next(relojes))

    llamadas_sesion = []

    def sesion_http_falsa():
        llamadas_sesion.append("llamada")
        return FakeSession(
            respuestas_post=[FakeResponse(status_code=200, headers={"Operation-Location": "https://op/x"})],
            respuestas_get=[
                FakeResponse(status_code=200, json_data={"status": "succeeded", "analyzeResult": {"content": "c"}})
            ],
        )

    monkeypatch.setattr(di_layout, "sesion_http", sesion_http_falsa)
    monkeypatch.setattr(di_layout.time, "sleep", lambda segundos: None)
    monkeypatch.setattr(sys, "argv", ["di_layout.py", "--lanzar", "--corpus", str(corpus)])

    di_layout.main()

    # 1 llamada inicial (arranque de main) + 1 llamada de renovación antes de doc2
    assert len(llamadas_sesion) == 2
