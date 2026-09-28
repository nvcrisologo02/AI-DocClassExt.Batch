"""DI Layout de DEV sobre los PDF sin markdown en BD. Dos modos:
  --estimar   solo recuento de documentos y páginas facturables (no llama a DI)
  --lanzar    llama a DI; solo tras autorización explícita del usuario
Uso: python di_layout.py --estimar | --lanzar [--corpus RUTA] [--max-paginas 5] [--solo-golden]"""
from __future__ import annotations

import argparse
import base64
import csv
import os
import time
from pathlib import Path

import requests

from comun import CACHE, CORPUS, DI_ENDPOINT, sesion_http
from texto import guardar_md, leer_md, ruta_md

API = "2024-11-30"
RENOVACION_INTERVALO_SEGUNDOS = 20 * 60
ESPERAS_TRANSITORIAS = (5, 15, 45)  # 429/5xx/ConnectionError/Timeout: hasta 3 reintentos


class ErrorDI(RuntimeError):
    """Error de DI con código HTTP explícito (o None), para la línea de error del log."""

    def __init__(self, mensaje: str, codigo: int | None = None):
        super().__init__(mensaje)
        self.codigo = codigo


class Autenticacion401(ErrorDI):
    """Señal interna de 401: nunca lleva token ni cabeceras, solo el código."""

    def __init__(self):
        super().__init__("401 no autorizado", codigo=401)


def paginas_facturables(paginas: int, max_paginas: int) -> int:
    return max_paginas if paginas < 0 else min(paginas, max_paginas)


def rango_paginas(paginas: int, max_paginas: int) -> str:
    return f"1-{paginas_facturables(paginas, max_paginas)}"


def ya_en_cache(sha: str) -> bool:
    """True si el markdown de sha ya está en CACHE/md: reanudar --lanzar no debe
    volver a facturar DI por un documento cuyo .md.gz ya existe (venga de un checkpoint
    anterior marcado di_dev o de una reutilización previa), da igual qué diga
    origen_texto en texto_origen.csv."""
    return ruta_md(sha).exists()


def escribir_origen(ruta, filas) -> None:
    """Escritura atómica de texto_origen.csv: fichero temporal en el mismo directorio
    y os.replace, para no dejar el CSV a medias si la ejecución se corta a mitad."""
    tmp = ruta.with_name(ruta.name + ".tmp")
    with tmp.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["sha256", "origen_texto", "caracteres"], delimiter=";")
        w.writeheader()
        w.writerows(filas)
    os.replace(tmp, ruta)


def analizar(s, pdf_bytes: bytes, rango: str) -> str:
    """Un único intento: POST + sondeo. Un 401 (token caducado) se señaliza con
    Autenticacion401 en vez de dejar que raise_for_status lo convierta en HTTPError,
    para que analizar_con_reintentos sepa que toca sesión nueva. Una respuesta sin los
    campos esperados (Operation-Location, status, analyzeResult.content) lanza ErrorDI
    (subclase de RuntimeError) con el código HTTP y como mucho 200 caracteres del
    cuerpo, en vez de un KeyError opaco."""
    url = (f"{DI_ENDPOINT}/documentintelligence/documentModels/prebuilt-layout:analyze"
           f"?api-version={API}&outputContentFormat=markdown&pages={rango}")
    r = s.post(url, json={"base64Source": base64.b64encode(pdf_bytes).decode()}, timeout=120)
    if r.status_code == 401:
        raise Autenticacion401()
    r.raise_for_status()
    op = r.headers.get("Operation-Location")
    if not op:
        raise ErrorDI(f"sin Operation-Location: {r.text[:200]!r}", codigo=r.status_code)
    for _ in range(120):
        time.sleep(2)
        resp = s.get(op, timeout=60)
        if resp.status_code == 401:
            raise Autenticacion401()
        resp.raise_for_status()
        j = resp.json()
        estado = j.get("status")
        if estado is None:
            raise ErrorDI(f"sin status: {resp.text[:200]!r}", codigo=resp.status_code)
        if estado == "succeeded":
            contenido = (j.get("analyzeResult") or {}).get("content")
            if contenido is None:
                raise ErrorDI(f"sin analyzeResult.content: {resp.text[:200]!r}", codigo=resp.status_code)
            return contenido
        if estado == "failed":
            raise ErrorDI(str(j.get("error")), codigo=resp.status_code)
    raise TimeoutError(op)


def _es_transitorio(exc: Exception) -> bool:
    if isinstance(exc, (requests.ConnectionError, requests.Timeout)):
        return True
    if isinstance(exc, requests.HTTPError) and exc.response is not None:
        codigo = exc.response.status_code
        return codigo == 429 or 500 <= codigo < 600
    return False


def _espera_transitoria(exc: Exception, intento: int) -> float:
    """Espera creciente (5, 15, 45 s) salvo que la respuesta traiga Retry-After
    numérico, que manda sobre la espera por defecto."""
    if isinstance(exc, requests.HTTPError) and exc.response is not None:
        retry_after = exc.response.headers.get("Retry-After")
        if retry_after is not None:
            try:
                return float(retry_after)
            except ValueError:
                pass
    return ESPERAS_TRANSITORIAS[intento]


def analizar_con_reintentos(s, pdf_bytes: bytes, rango: str, dormir=None):
    """Envoltura de analizar() para un documento:
    - 401 (POST o GET de sondeo): sesión nueva vía sesion_http() y reintento del
      documento completo, como mucho 1 vez.
    - 429, 5xx, ConnectionError o Timeout: hasta 3 reintentos con espera creciente
      (inyectable por `dormir`, por defecto time.sleep, para no dormir de verdad en tests).
    Devuelve (contenido, sesión a usar en los siguientes documentos del bucle)."""
    if dormir is None:
        dormir = time.sleep
    reintento_401_usado = False
    intento_transitorio = 0
    while True:
        try:
            return analizar(s, pdf_bytes, rango), s
        except Autenticacion401:
            if reintento_401_usado:
                raise
            reintento_401_usado = True
            s = sesion_http()
        except (requests.ConnectionError, requests.Timeout, requests.HTTPError) as e:
            if not _es_transitorio(e) or intento_transitorio >= len(ESPERAS_TRANSITORIAS):
                raise
            dormir(_espera_transitoria(e, intento_transitorio))
            intento_transitorio += 1


def _codigo_http(exc: Exception) -> str:
    codigo = getattr(exc, "codigo", None)
    if codigo is None:
        resp = getattr(exc, "response", None)
        codigo = getattr(resp, "status_code", None) if resp is not None else None
    return str(codigo) if codigo is not None else "-"


def main() -> None:
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--estimar", action="store_true")
    g.add_argument("--lanzar", action="store_true")
    ap.add_argument("--corpus", default=str(CORPUS))
    ap.add_argument("--max-paginas", type=int, default=5)
    ap.add_argument("--solo-golden", action="store_true")
    args = ap.parse_args()

    pend = list(csv.DictReader((CACHE / "pendientes_di.csv").open(encoding="utf-8-sig"), delimiter=";"))
    if args.solo_golden:
        inv = {r["sha256"]: r["particion"] for r in csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";")}
        pend = [p for p in pend if inv.get(p["sha256"]) == "golden"]
    total = sum(paginas_facturables(int(p["paginas"]), args.max_paginas) for p in pend)
    print(f"documentos: {len(pend)} | páginas facturables (recorte {args.max_paginas}): {total}")
    if args.estimar:
        return

    corpus_path = Path(args.corpus)
    if not corpus_path.is_dir():
        print(f"corpus no accesible: {corpus_path}")
        raise SystemExit(2)

    s = sesion_http()
    origen_csv = CACHE / "texto_origen.csv"
    filas = list(csv.DictReader(origen_csv.open(encoding="utf-8-sig"), delimiter=";"))
    idx = {f["sha256"]: f for f in filas}
    total_pend = len(pend)
    ok = err = vacios = reutilizados = 0
    ultima_renovacion = time.monotonic()
    for p in pend:
        sha = p["sha256"]
        fila = idx[sha]
        if ya_en_cache(sha):
            md = leer_md(sha)
            fila.update(origen_texto="di_dev", caracteres=str(len(md)))
            reutilizados += 1
        else:
            ahora = time.monotonic()
            if ahora - ultima_renovacion > RENOVACION_INTERVALO_SEGUNDOS:
                s = sesion_http()  # renovación preventiva: comprobada antes de cada documento
                ultima_renovacion = ahora
            try:
                md, s = analizar_con_reintentos(
                    s, (corpus_path / p["rel_path"]).read_bytes(), rango_paginas(int(p["paginas"]), args.max_paginas)
                )
                if md.strip():
                    guardar_md(sha, md)
                    fila.update(origen_texto="di_dev", caracteres=str(len(md)))
                    ok += 1
                else:
                    vacios += 1
            except Exception as e:  # se registra y se sigue; el documento queda sin_texto
                err += 1
                print("error", p["rel_path"], type(e).__name__, _codigo_http(e))
        procesados = ok + reutilizados + err + vacios
        if procesados % 100 == 0:
            print(
                f"progreso {procesados}/{total_pend} | ok {ok} | reutilizados {reutilizados} "
                f"| vacíos {vacios} | errores {err}",
                flush=True,
            )
            escribir_origen(origen_csv, filas)  # checkpoint: como mucho se pierden 100 marcas si se corta
    escribir_origen(origen_csv, filas)
    print(f"DI ok {ok} | reutilizados {reutilizados} | vacíos {vacios} | errores {err}")


if __name__ == "__main__":
    main()
