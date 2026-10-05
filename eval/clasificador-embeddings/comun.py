"""Utilidades comunes de la prueba de clasificador por embeddings (AB#100719).

Solo lectura: tokens de Entra con az CLI, hash de ficheros y sesión HTTP que usa el
almacén de certificados de Windows (el proxy corporativo inspecciona TLS)."""
from __future__ import annotations

import hashlib
import subprocess
from pathlib import Path

import requests
import truststore

truststore.inject_into_ssl()

CACHE = Path("C:/temp/MVP/spike-embeddings-cache")
CORPUS = Path("H:/Documentia/ParaNacho/Class")
EVAL = Path(__file__).resolve().parents[1]
GOLDEN_CSV = EVAL / "golden.csv"
BASELINE_CSV = EVAL / "runs" / "BASELINE-GPT4OMINI-DEV" / "results.csv"
INFORME_FICHERO_CSV = EVAL / "runs" / "BASELINE-FINAL-CONSOLIDADO" / "informe_por_fichero.csv"

EMB_ENDPOINT = "https://srbaisrv02devdocai.cognitiveservices.azure.com"
EMB_DEPLOYMENT = "text-embedding-3-large-010650"
CHAT_ENDPOINT = "https://srbaisrv01devdocai.cognitiveservices.azure.com"
CHAT_DEPLOYMENT = "gpt-4o-mini"  # es gpt-4.1-mini 2025-04-14
DI_ENDPOINT = "https://srbdidevdocai.cognitiveservices.azure.com"
RES_COGNITIVE = "https://cognitiveservices.azure.com"
RES_SQL = "https://database.windows.net/"


def sha256_fichero(p: Path) -> str:
    h = hashlib.sha256()
    with p.open("rb") as f:
        for bloque in iter(lambda: f.read(1 << 20), b""):
            h.update(bloque)
    return h.hexdigest()


def token(resource: str) -> str:
    """Token de Entra de la sesión az; nunca se imprime."""
    return subprocess.run(
        ["az", "account", "get-access-token", "--resource", resource, "--query", "accessToken", "-o", "tsv"],
        capture_output=True, text=True, check=True, shell=True,
    ).stdout.strip()


def sesion_http() -> requests.Session:
    s = requests.Session()
    s.headers["Authorization"] = f"Bearer {token(RES_COGNITIVE)}"
    return s
