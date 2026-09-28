"""Enfoque C: gpt-4.1-mini (deployment 'gpt-4o-mini' de DEV) con logprobs, prompt compacto
con el catálogo TDN1. Calibra y fija umbral con 300 documentos de 'cal' y evalúa en 'golden'.

Desviación de la especificación (se señala en el informe): no se reproduce el prompt de
producción, que vive en PromptTemplates de BD y depende del pipeline. Mide si las logprobs
de gpt-4.1-mini dan una confianza calibrada, no su acierto frente al prompt real.

Reintentos: mismo patrón que embeddings.py (401 renueva el token de la MISMA sesión y
reintenta una vez; un segundo 401 seguido propaga Autenticacion401; 429 respeta
Retry-After; 5xx/ConnectionError/Timeout con reintento acotado ESPERAS_TRANSITORIAS). Un
fallo no recuperable en un documento (por ejemplo 400 por filtro de contenido) no tumba
la ejecución: ese documento queda con C_tdn1 = "" y C_conf = 0.0 y se cuenta en
metricas.json como error, con el código HTTP y nunca con el contenido del documento.

El nombre del fichero nunca entra en el prompt: solo el markdown.
Uso: python logprobs_gpt.py"""
from __future__ import annotations

import csv
import json
import math
import random
import time
from datetime import datetime

import requests

from comun import CACHE, CHAT_DEPLOYMENT, CHAT_ENDPOINT, EVAL, RES_COGNITIVE, sesion_http, token
from embeddings import Autenticacion401, ESPERAS_TRANSITORIAS, recortar
from metricas import acierto, cobertura_y_acierto, ece, umbral_para_acierto
from modelos import Calibrador
from texto import leer_md

API = "2024-10-21"


class FalloDocumento(RuntimeError):
    """Error HTTP no recuperable en un documento (por ejemplo 400 por filtro de
    contenido): no se reintenta ni tumba la ejecución. Lleva solo el código HTTP,
    nunca el contenido del documento."""

    def __init__(self, status_code: int):
        self.status_code = status_code
        super().__init__(f"fallo no recuperable en el documento: HTTP {status_code}")


def confianza_de(logprobs) -> float:
    return math.exp(sum(t["logprob"] for t in logprobs)) if logprobs else 0.0


def prompt_sistema() -> str:
    cat = json.loads((EVAL / "catalogotdn1.json").read_text(encoding="utf-8-sig"))
    lineas = [f"{c['Codigo']}: {c['Nombre']}. {c['Descripcion']}" for c in cat]
    return ("Clasifica el documento en UNA familia documental. Responde SOLO con el código de 4 letras, "
            "sin nada más.\n\nFamilias:\n" + "\n".join(lineas))


def _llamar_chat(s, cuerpo):
    """Un POST de chat completions con el mismo patrón de reintentos que embeddings._llamar:
    - 401: renueva el token reescribiendo Authorization de la MISMA sesión y reintenta
      una vez; un segundo 401 seguido propaga Autenticacion401.
    - 429: respeta Retry-After.
    - 5xx / ConnectionError / Timeout: reintento acotado con espera creciente
      (ESPERAS_TRANSITORIAS).
    - Cualquier otro 4xx (por ejemplo 400 de filtro de contenido): no recuperable,
      propaga FalloDocumento sin reintentar."""
    url = f"{CHAT_ENDPOINT}/openai/deployments/{CHAT_DEPLOYMENT}/chat/completions?api-version={API}"
    reintento_401 = False
    intento_transitorio = 0
    while True:
        try:
            r = s.post(url, json=cuerpo, timeout=120)
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
        if 500 <= r.status_code < 600:
            if intento_transitorio >= len(ESPERAS_TRANSITORIAS):
                r.raise_for_status()
            time.sleep(ESPERAS_TRANSITORIAS[intento_transitorio])
            intento_transitorio += 1
            continue
        if r.status_code >= 400:
            raise FalloDocumento(r.status_code)
        return r.json()


def clasificar(s, sistema, md):
    cuerpo = {"messages": [{"role": "system", "content": sistema},
                           {"role": "user", "content": recortar(md, 40000)}],
              "temperature": 0, "max_tokens": 4, "logprobs": True}
    ch = _llamar_chat(s, cuerpo)["choices"][0]
    return ch["message"]["content"].strip()[:4].upper(), confianza_de(ch["logprobs"]["content"])


def correr(s, sistema, filas):
    """Devuelve (filas de resultado, errores). Un fallo no recuperable en un documento
    no tumba la ejecución: el documento queda con C_tdn1 = "" y C_conf = 0.0 y el error
    se registra con su ruta relativa y código HTTP, nunca con el contenido."""
    out, errores = [], []
    for r in filas:
        md = leer_md(r["sha256"])
        cod, conf = "", 0.0
        if md:
            try:
                cod, conf = clasificar(s, sistema, md)
            except FalloDocumento as e:
                errores.append({"rel_path": r["rel_path"], "status_code": e.status_code})
        out.append({"rel_path": r["rel_path"], "exp_tdn1": r["tdn1"], "C_tdn1": cod, "C_conf": conf})
    return out, errores


def main() -> None:
    inv = list(csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";"))
    cal = sorted((r for r in inv if r["particion"] == "cal"), key=lambda r: r["sha256"])
    cal = random.Random(42).sample(cal, min(300, len(cal)))
    golden = [r for r in inv if r["particion"] == "golden"]
    s, sistema = sesion_http(), prompt_sistema()
    rc, errores_cal = correr(s, sistema, cal)
    calib = Calibrador().fit([f["C_conf"] for f in rc], [f["C_tdn1"] == f["exp_tdn1"] for f in rc])
    t = umbral_para_acierto(calib.aplicar([f["C_conf"] for f in rc]), [f["C_tdn1"] == f["exp_tdn1"] for f in rc], 0.90)
    s = sesion_http()
    rg, errores_golden = correr(s, sistema, golden)
    conf = calib.aplicar([f["C_conf"] for f in rg])
    corr = [f["C_tdn1"] == f["exp_tdn1"] for f in rg]
    cob, acc = cobertura_y_acierto(conf, corr, t)
    res = {"n": len(rg), "tdn1": acierto([f["exp_tdn1"] for f in rg], [f["C_tdn1"] for f in rg]),
           "ece_bruta": ece([f["C_conf"] for f in rg], corr), "ece_cal": ece(conf, corr),
           "umbral_cal": t, "cobertura": cob, "acierto_cubierto": acc,
           "errores": {"cal_n": len(errores_cal), "golden_n": len(errores_golden),
                       "golden_codigos": [e["status_code"] for e in errores_golden]}}
    salida = EVAL / "runs" / f"{datetime.now():%Y%m%d-%H%M%S}-logprobs-gpt"
    salida.mkdir(parents=True)
    with (salida / "resultados.csv").open("w", newline="", encoding="utf-8-sig") as fh:
        w = csv.DictWriter(fh, list(rg[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(rg)
    (salida / "metricas.json").write_text(json.dumps(res, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(res, indent=2, ensure_ascii=False))
    print("escrito en", salida)


if __name__ == "__main__":
    main()
