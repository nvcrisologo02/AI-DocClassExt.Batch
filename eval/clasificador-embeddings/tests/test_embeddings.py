import sys
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import embeddings  # noqa: E402  (sys.path se ajusta arriba)
from embeddings import recortar  # noqa: E402


def test_recorte_por_caracteres():
    assert recortar("a" * 30000) == "a" * 24000


def test_texto_corto_intacto_y_colapsa_espacios():
    assert recortar("hola   \n\n\n mundo") == "hola mundo"


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
    """Doble de requests.Session: sirve respuestas de post() en orden, sin red. Expone
    headers como dict real para poder comprobar la renovación de token in-place (la
    resolución 1 exige reescribir la cabecera de la MISMA sesión, no crear otra)."""

    def __init__(self, respuestas):
        self._respuestas = list(respuestas)
        self.headers = {"Authorization": "Bearer token-viejo"}
        self.llamadas = 0
        self.autorizaciones_vistas = []
        self.pedidos = []

    def post(self, url, json=None, timeout=None):
        self.llamadas += 1
        self.autorizaciones_vistas.append(self.headers.get("Authorization"))
        self.pedidos.append(json)
        return self._respuestas.pop(0)


def test_401_renueva_token_en_la_misma_sesion_y_reintenta_una_vez(monkeypatch):
    """Resolución 1: un 401 reescribe Authorization de la MISMA sesión (nunca crea
    sesión nueva) y reintenta una vez; el documento acaba bien."""
    resp_401 = FakeResponse(status_code=401)
    resp_ok = FakeResponse(
        status_code=200,
        json_data={"data": [{"index": 0, "embedding": [0.1, 0.2]}], "usage": {"prompt_tokens": 5}},
    )
    s = FakeSession([resp_401, resp_ok])
    monkeypatch.setattr(embeddings, "token", lambda recurso: "token-nuevo")

    emb, tokens = embeddings.pedir(s, ["hola"])

    assert emb == [[0.1, 0.2]]
    assert tokens == 5
    assert s.llamadas == 2
    assert s.autorizaciones_vistas == ["Bearer token-viejo", "Bearer token-nuevo"]
    assert s.headers["Authorization"] == "Bearer token-nuevo"


def test_401_persistente_propaga_tras_un_reintento(monkeypatch):
    """Resolución 1: un segundo 401 seguido propaga el error, sin bucle infinito."""
    s = FakeSession([FakeResponse(status_code=401), FakeResponse(status_code=401)])
    monkeypatch.setattr(embeddings, "token", lambda recurso: "token-nuevo")

    with pytest.raises(embeddings.Autenticacion401):
        embeddings.pedir(s, ["hola"])

    assert s.llamadas == 2  # exactamente 1 reintento, no más


def test_maximum_context_length_reintenta_texto_a_texto_y_recorta_solo_el_que_excede(monkeypatch):
    """Resolución 2: ante 400 'maximum context length' no se recorta el lote entero;
    se reintenta texto a texto y solo el texto que excede se parte a la mitad hasta
    que entra. El recorte de un documento no depende del lote en que cae."""
    largo = "x" * 100
    resp_400_lote = FakeResponse(status_code=400, text="exceeds the maximum context length")
    resp_ok_corto = FakeResponse(
        status_code=200, json_data={"data": [{"index": 0, "embedding": [0.1]}], "usage": {"prompt_tokens": 3}}
    )
    resp_400_largo = FakeResponse(status_code=400, text="exceeds the maximum context length")
    resp_ok_largo = FakeResponse(
        status_code=200, json_data={"data": [{"index": 0, "embedding": [0.2]}], "usage": {"prompt_tokens": 4}}
    )
    s = FakeSession([resp_400_lote, resp_ok_corto, resp_400_largo, resp_ok_largo])
    monkeypatch.setattr(embeddings.time, "sleep", lambda segundos: None)

    emb, tokens = embeddings.pedir(s, ["corto", largo])

    assert emb == [[0.1], [0.2]]
    assert tokens == 7
    assert s.pedidos[0] == {"input": ["corto", largo]}  # intento de lote completo
    assert s.pedidos[1] == {"input": ["corto"]}  # texto a texto: el corto entra a la primera
    assert s.pedidos[2] == {"input": [largo]}  # el largo excede otra vez tal cual
    assert s.pedidos[3] == {"input": [largo[: len(largo) // 2]]}  # partido a la mitad, entra


def test_separar_vacios_deja_fuera_los_textos_vacios_tras_recortar():
    """Resolución 3: los textos vacíos tras recortar() no se envían y quedan sin vector."""
    con, vacios = embeddings.separar_vacios(["a", "b", "c"], {"a": "hola", "b": "", "c": "mundo"})

    assert con == ["a", "c"]
    assert vacios == ["b"]
