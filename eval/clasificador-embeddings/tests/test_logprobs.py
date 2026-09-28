import math
import sys
from pathlib import Path

import pytest
import requests

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import logprobs_gpt  # noqa: E402  (sys.path se ajusta arriba)
from embeddings import Autenticacion401  # noqa: E402
from logprobs_gpt import FalloDocumento, confianza_de, correr  # noqa: E402


def test_confianza_producto_de_tokens():
    lp = [{"token": "NO", "logprob": math.log(0.9)}, {"token": "TS", "logprob": math.log(0.5)}]
    assert math.isclose(confianza_de(lp), 0.45)


def test_sin_tokens():
    assert confianza_de([]) == 0.0


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
        efecto = self._respuestas.pop(0)
        if isinstance(efecto, Exception):
            raise efecto
        return efecto


def _resp_ok(codigo="NOTS", logprob=math.log(0.9)):
    return FakeResponse(
        status_code=200,
        json_data={
            "choices": [{
                "message": {"content": codigo},
                "logprobs": {"content": [{"token": codigo, "logprob": logprob}]},
            }]
        },
    )


def test_clasificar_devuelve_codigo_y_confianza():
    s = FakeSession([_resp_ok("NOTS", math.log(0.9))])

    cod, conf = logprobs_gpt.clasificar(s, "sistema", "hola mundo")

    assert cod == "NOTS"
    assert math.isclose(conf, 0.9)


def test_401_renueva_token_en_la_misma_sesion_y_reintenta_una_vez(monkeypatch):
    """Resolución 1: un 401 reescribe Authorization de la MISMA sesión (nunca crea
    sesión nueva) y reintenta una vez; el documento acaba bien."""
    s = FakeSession([FakeResponse(status_code=401), _resp_ok("NOTS", math.log(0.8))])
    monkeypatch.setattr(logprobs_gpt, "token", lambda recurso: "token-nuevo")

    cod, conf = logprobs_gpt.clasificar(s, "sistema", "hola")

    assert cod == "NOTS"
    assert math.isclose(conf, 0.8)
    assert s.llamadas == 2
    assert s.autorizaciones_vistas == ["Bearer token-viejo", "Bearer token-nuevo"]
    assert s.headers["Authorization"] == "Bearer token-nuevo"


def test_401_persistente_propaga_tras_un_reintento(monkeypatch):
    """Resolución 1: un segundo 401 seguido propaga el error, sin bucle infinito."""
    s = FakeSession([FakeResponse(status_code=401), FakeResponse(status_code=401)])
    monkeypatch.setattr(logprobs_gpt, "token", lambda recurso: "token-nuevo")

    with pytest.raises(Autenticacion401):
        logprobs_gpt.clasificar(s, "sistema", "hola")

    assert s.llamadas == 2  # exactamente 1 reintento, no más


def test_429_respeta_retry_after_y_reintenta(monkeypatch):
    s = FakeSession([FakeResponse(status_code=429, headers={"retry-after": "7"}), _resp_ok("CERA", math.log(0.6))])
    esperas = []
    monkeypatch.setattr(logprobs_gpt.time, "sleep", lambda segundos: esperas.append(segundos))

    cod, conf = logprobs_gpt.clasificar(s, "sistema", "hola")

    assert cod == "CERA"
    assert esperas == [7]
    assert s.llamadas == 2


def test_5xx_reintenta_con_espera_creciente_y_acaba_ok(monkeypatch):
    s = FakeSession([FakeResponse(status_code=500, text="server error"), _resp_ok("HOJA", math.log(0.7))])
    esperas = []
    monkeypatch.setattr(logprobs_gpt.time, "sleep", lambda segundos: esperas.append(segundos))

    cod, conf = logprobs_gpt.clasificar(s, "sistema", "hola")

    assert cod == "HOJA"
    assert esperas == [5]
    assert s.llamadas == 2


def test_5xx_agota_los_3_reintentos_y_propaga(monkeypatch):
    respuestas = [FakeResponse(status_code=503, text="unavailable") for _ in range(4)]
    s = FakeSession(respuestas)
    esperas = []
    monkeypatch.setattr(logprobs_gpt.time, "sleep", lambda segundos: esperas.append(segundos))

    with pytest.raises(requests.HTTPError):
        logprobs_gpt.clasificar(s, "sistema", "hola")

    assert s.llamadas == 4  # intento inicial + 3 reintentos
    assert esperas == [5, 15, 45]


def test_connectionerror_transitorio_reintenta_y_acaba_ok(monkeypatch):
    s = FakeSession([requests.ConnectionError("sin red"), _resp_ok("SERE", math.log(0.55))])
    esperas = []
    monkeypatch.setattr(logprobs_gpt.time, "sleep", lambda segundos: esperas.append(segundos))

    cod, conf = logprobs_gpt.clasificar(s, "sistema", "hola")

    assert cod == "SERE"
    assert esperas == [5]
    assert s.llamadas == 2


def test_400_lanza_fallodocumento_sin_reintentar():
    """Resolución 2: un 400 (p. ej. filtro de contenido) es no recuperable: no se
    reintenta y se propaga con el código HTTP, nunca con el contenido del documento."""
    s = FakeSession([FakeResponse(status_code=400, text="content_filter")])

    with pytest.raises(FalloDocumento) as exc:
        logprobs_gpt.clasificar(s, "sistema", "hola")

    assert exc.value.status_code == 400
    assert s.llamadas == 1


def test_correr_no_tumba_la_ejecucion_ante_un_fallo_no_recuperable_y_lo_cuenta_como_error(monkeypatch):
    """Resolución 2: un documento con un fallo no recuperable queda con C_tdn1 = "" y
    C_conf = 0.0, y el resto de la ejecución sigue; el error se cuenta con su código
    HTTP, sin contenido del documento."""
    filas = [
        {"rel_path": "a.pdf", "tdn1": "NOTS", "sha256": "sha-a"},
        {"rel_path": "b.pdf", "tdn1": "CERA", "sha256": "sha-b"},
    ]
    textos = {"sha-a": "texto a", "sha-b": "texto b"}
    monkeypatch.setattr(logprobs_gpt, "leer_md", lambda sha: textos[sha])

    llamadas = iter([
        FalloDocumento(400),
        ("CERA", 0.77),
    ])

    def fake_clasificar(s, sistema, md):
        efecto = next(llamadas)
        if isinstance(efecto, Exception):
            raise efecto
        return efecto

    monkeypatch.setattr(logprobs_gpt, "clasificar", fake_clasificar)

    salida, errores = correr(object(), "sistema", filas)

    assert salida[0]["C_tdn1"] == ""
    assert salida[0]["C_conf"] == 0.0
    assert salida[1]["C_tdn1"] == "CERA"
    assert math.isclose(salida[1]["C_conf"], 0.77)
    assert errores == [{"rel_path": "a.pdf", "status_code": 400}]
    # nunca se filtra el contenido del documento en el registro de errores
    assert "texto a" not in str(errores)


def test_correr_documento_sin_markdown_queda_vacio_sin_llamar(monkeypatch):
    filas = [{"rel_path": "c.pdf", "tdn1": "NOTS", "sha256": "sha-c"}]
    llamado = []
    monkeypatch.setattr(logprobs_gpt, "leer_md", lambda sha: None)
    monkeypatch.setattr(logprobs_gpt, "clasificar", lambda s, sistema, md: llamado.append(1))

    salida, errores = correr(object(), "sistema", filas)

    assert salida == [{"rel_path": "c.pdf", "exp_tdn1": "NOTS", "C_tdn1": "", "C_conf": 0.0}]
    assert errores == []
    assert llamado == []
