"""Embeddings text-embedding-3-large (DEV, srbaisrv02devdocai) del markdown de cada documento.

Se toma el principio del documento (~8.000 tokens ≈ 24.000 caracteres). Si el servicio
rechaza la entrada por longitud, se reintenta texto a texto y solo el que excede se
recorta a la mitad de forma repetida (nunca el lote entero: el recorte de un documento
no puede depender del lote en que cae). Un 401 renueva el token reescribiendo la
cabecera de la MISMA sesión (nunca se crea sesión nueva dentro de una llamada, y nunca
se imprime el token ni las cabeceras); un segundo 401 seguido propaga el error. Los
textos vacíos tras recortar() no se envían, se cuentan y quedan sin vector. Reanudable:
solo calcula los hashes que faltan en embeddings.npz.
Uso: python embeddings.py"""
from __future__ import annotations

import csv
import re
import time

import numpy as np
import requests

from comun import CACHE, EMB_DEPLOYMENT, EMB_ENDPOINT, RES_COGNITIVE, sesion_http, token
from texto import leer_md

API = "2024-10-21"
LOTE = 16
NPZ = CACHE / "embeddings.npz"
ESPERAS_TRANSITORIAS = (5, 15, 45)  # 5xx/ConnectionError/Timeout: hasta 3 reintentos


class Autenticacion401(RuntimeError):
    """Segundo 401 consecutivo dentro de la misma llamada: la sesión no se pudo
    renovar. Nunca lleva token ni cabeceras, solo el mensaje."""

    def __init__(self):
        super().__init__("401 no autorizado tras renovar el token")


class ContextoExcedido(RuntimeError):
    """400 'maximum context length': el servicio rechaza la entrada por longitud."""


def recortar(md: str, max_chars: int = 24000) -> str:
    return re.sub(r"\s+", " ", md).strip()[:max_chars]


def cargar():
    d = np.load(NPZ, allow_pickle=False)
    return list(d["shas"]), d["X"]


def separar_vacios(lote_hashes, textos):
    """textos: dict sha -> markdown ya recortado. Los vacíos no se envían y quedan
    sin vector (se cuentan e informan en main)."""
    con = [h for h in lote_hashes if textos[h]]
    vacios = [h for h in lote_hashes if not textos[h]]
    return con, vacios


def _llamar(s, textos):
    """Un POST de embeddings con reintentos:
    - 401: renueva el token reescribiendo Authorization de la MISMA sesión y reintenta
      una vez; un segundo 401 seguido propaga Autenticacion401.
    - 429: respeta Retry-After.
    - 5xx / ConnectionError / Timeout: reintento acotado con espera creciente
      (ESPERAS_TRANSITORIAS).
    - 400 'maximum context length': propaga ContextoExcedido sin reintentar aquí
      (lo gestiona pedir()/​_pedir_uno()).
    Devuelve (embeddings ordenados por index, tokens del prompt)."""
    url = f"{EMB_ENDPOINT}/openai/deployments/{EMB_DEPLOYMENT}/embeddings?api-version={API}"
    reintento_401 = False
    intento_transitorio = 0
    while True:
        try:
            r = s.post(url, json={"input": textos}, timeout=120)
        except (requests.ConnectionError, requests.Timeout):
            if intento_transitorio >= len(ESPERAS_TRANSITORIAS):
                raise
            time.sleep(ESPERAS_TRANSITORIAS[intento_transitorio])
            intento_transitorio += 1
            continue
        if r.status_code == 401:
            if reintento_401:
                raise Autenticacion401()
            reintento_401 = True
            s.headers["Authorization"] = f"Bearer {token(RES_COGNITIVE)}"
            continue
        if r.status_code == 429:
            time.sleep(int(r.headers.get("retry-after", 10)))
            continue
        if r.status_code == 400 and ("maximum context length" in r.text or "maximum input length" in r.text):
            # El endpoint de DEV (verificado el 2026-09-28) no devuelve literalmente
            # "maximum context length" sino "maximum input length is 8192 tokens".
            # Se comprueban ambas cadenas: la del plan y la real observada.
            raise ContextoExcedido(r.text[:200])
        if 500 <= r.status_code < 600:
            if intento_transitorio >= len(ESPERAS_TRANSITORIAS):
                r.raise_for_status()
            time.sleep(ESPERAS_TRANSITORIAS[intento_transitorio])
            intento_transitorio += 1
            continue
        r.raise_for_status()
        j = r.json()
        return [e["embedding"] for e in sorted(j["data"], key=lambda e: e["index"])], j["usage"]["prompt_tokens"]


def _pedir_uno(s, texto):
    """Pide el embedding de un único texto; si el servicio lo sigue rechazando por
    longitud, lo parte a la mitad repetidamente hasta que entra."""
    while True:
        try:
            emb, tokens = _llamar(s, [texto])
            return emb[0], tokens
        except ContextoExcedido:
            if len(texto) <= 1:
                raise
            texto = texto[: len(texto) // 2]


def pedir(s, textos):
    """Embeddings de un lote. Ante 'maximum context length' no se recorta el lote
    entero: se reintenta texto a texto y solo el texto que excede se recorta (ver
    _pedir_uno)."""
    if not textos:
        return [], 0
    try:
        return _llamar(s, textos)
    except ContextoExcedido:
        pass
    embeddings, tokens_total = [], 0
    for t in textos:
        emb, tokens = _pedir_uno(s, t)
        embeddings.append(emb)
        tokens_total += tokens
    return embeddings, tokens_total


def main() -> None:
    con_texto = [r["sha256"] for r in csv.DictReader((CACHE / "texto_origen.csv").open(encoding="utf-8-sig"), delimiter=";")
                 if r["origen_texto"] != "sin_texto"]
    shas, X = (cargar() if NPZ.exists() else ([], np.zeros((0, 3072), np.float32)))
    hechos = set(shas)
    faltan = [h for h in con_texto if h not in hechos]
    print("por calcular:", len(faltan), flush=True)
    s = sesion_http()
    nuevos, vecs = [], []
    vacios_total = []
    uso = (CACHE / "embeddings_uso.csv").open("a", newline="", encoding="utf-8")
    procesados = 0
    for i in range(0, len(faltan), LOTE):
        lote = faltan[i:i + LOTE]
        textos = {h: recortar(leer_md(h) or "") for h in lote}
        con, vacios = separar_vacios(lote, textos)
        vacios_total += vacios
        if con:
            t0 = time.perf_counter()
            emb, tokens = pedir(s, [textos[h] for h in con])
            uso.write(f"{len(con)};{tokens};{time.perf_counter() - t0:.3f}\n")
            uso.flush()  # visibilidad en ejecuciones largas (>=70 min): sin esto los
            # tiempos de lote quedan en el buffer de Python y no se pueden auditar en
            # vivo, el mismo tipo de opacidad que tumbó 851 documentos en la tarea 3.
            nuevos += con
            vecs += emb
        procesados += len(lote)
        if procesados % (LOTE * 20) == 0 or i + LOTE >= len(faltan):
            print(f"progreso {procesados}/{len(faltan)}", flush=True)
        if nuevos and (len(nuevos) % 800 == 0 or i + LOTE >= len(faltan)):
            V = np.asarray(vecs, np.float32)
            V /= np.linalg.norm(V, axis=1, keepdims=True)
            shas, X = shas + nuevos, np.vstack([X, V])
            np.savez(NPZ, shas=np.array(shas), X=X)
            nuevos, vecs = [], []
            s = sesion_http()
            print("guardados:", len(shas), flush=True)
    uso.close()
    if vacios_total:
        print(f"vacíos tras recortar: {len(vacios_total)} | primeros 10: {vacios_total[:10]}", flush=True)


if __name__ == "__main__":
    main()
