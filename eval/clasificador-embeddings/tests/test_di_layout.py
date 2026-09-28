import csv
import sys
from pathlib import Path

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
