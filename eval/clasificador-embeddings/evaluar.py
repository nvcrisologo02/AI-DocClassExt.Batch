"""Entrena A y B con 'train', calibra y fija umbrales con 'cal', y evalúa en 'golden'
frente al baseline GPT. Escribe resultados.csv y metricas.json en eval/runs/.
Uso: python evaluar.py"""
from __future__ import annotations

import csv
import json
import time
from datetime import datetime

import numpy as np

from comun import BASELINE_CSV, CACHE, EVAL, INFORME_FICHERO_CSV
from embeddings import cargar
from metricas import acierto, cobertura_y_acierto, ece, mcnemar, umbral_para_acierto
from modelos import ClasificadorKNN, ClasificadorLR, Prediccion, calibrar


def hibrido(pred: Prediccion, conf_cal: float, t: float, gpt_tdn1: str, gpt_tdn2: str):
    if conf_cal < t:
        return gpt_tdn1, gpt_tdn2, False
    tdn2 = pred.tdn2 or (gpt_tdn2 if gpt_tdn1 == pred.tdn1 else "")
    return pred.tdn1, tdn2, True


def leer(p):
    return list(csv.DictReader(p.open(encoding="utf-8-sig"), delimiter=";"))


def metricas_vista(filas, clave):
    y1 = [f["exp_tdn1"] for f in filas]
    y2 = [f["exp_tdn2"] for f in filas]
    ok_gpt = [f["gpt_tdn1"] == f["exp_tdn1"] for f in filas]
    conf_gpt = [f["gpt_conf"] for f in filas]
    # El GPT no tiene conjunto de calibración: su umbral se fija sobre el propio golden, así
    # que su cobertura es optimista y se etiqueta como tal en el informe.
    t_gpt = umbral_para_acierto(conf_gpt, ok_gpt, 0.90)
    out = {"n": len(filas), "gpt": {"tdn1": acierto(y1, [f["gpt_tdn1"] for f in filas]),
                                    "tdn2": acierto(y2, [f["gpt_tdn2"] for f in filas]),
                                    "ece_tdn1": ece(conf_gpt, ok_gpt), "umbral_golden_optimista": t_gpt,
                                    "cobertura_optimista": cobertura_y_acierto(conf_gpt, ok_gpt, t_gpt)[0]}}
    for m in ("A", "B"):
        corr = [f[f"{m}_tdn1"] == f["exp_tdn1"] for f in filas]
        conf = [f[f"{m}_conf"] for f in filas]
        t = clave[m]["umbral"]
        cob, acc = cobertura_y_acierto(conf, corr, t)
        h1 = [f[f"H{m}_tdn1"] for f in filas]
        b, c, p = mcnemar([a == e for a, e in zip(h1, y1)], ok_gpt)
        out[m] = {"tdn1": acierto(y1, [f[f"{m}_tdn1"] for f in filas]),
                  "tdn2": acierto(y2, [f[f"{m}_tdn2"] for f in filas]),
                  "ece_tdn1": ece(conf, corr), "umbral_cal": t, "cobertura": cob, "acierto_cubierto": acc,
                  "hibrido_tdn1": acierto(y1, h1), "hibrido_tdn2": acierto(y2, [f[f"H{m}_tdn2"] for f in filas]),
                  "mcnemar_b_c_p": [b, c, p]}
    return out


def main() -> None:
    inv = {r["sha256"]: r for r in leer(CACHE / "inventario.csv")}
    origen = {r["sha256"]: r["origen_texto"] for r in leer(CACHE / "texto_origen.csv")}
    base = {r["rel_path"].lower(): r for r in leer(BASELINE_CSV)}
    proced = {r["filename"].lower() for r in leer(INFORME_FICHERO_CSV) if r["resultado"] == "PROCEDENCIA"}
    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}

    def conjunto(nombre):
        hs = [h for h, r in inv.items() if r["particion"] == nombre and h in pos]
        return hs, X[[pos[h] for h in hs]], [inv[h]["tdn1"] for h in hs], [inv[h]["tdn2"] for h in hs]

    h_tr, X_tr, t1_tr, t2_tr = conjunto("train")
    h_ca, X_ca, t1_ca, _ = conjunto("cal")
    print(f"train {len(h_tr)} | cal {len(h_ca)}")
    modelos, cal, umbral, lat = {}, {}, {}, {}
    for nombre, m in (("A", ClasificadorLR()), ("B", ClasificadorKNN())):
        modelos[nombre] = m.fit(X_tr, t1_tr, t2_tr)
        cal[nombre] = calibrar(m, X_ca, t1_ca)
        p = m.predecir(X_ca)
        conf = cal[nombre].aplicar([a.conf_tdn1 for a in p])
        umbral[nombre] = umbral_para_acierto(conf, [a.tdn1 == t for a, t in zip(p, t1_ca)], 0.90)

    filas = []
    golden = [r for r in inv.values() if r["particion"] == "golden"]
    for r in golden:
        b = base.get(r["rel_path"].lower())
        if b is None:
            continue
        f = {"rel_path": r["rel_path"], "exp_tdn1": r["tdn1"], "exp_tdn2": r["tdn2"],
             "gpt_tdn1": b["predicted_tdn1"], "gpt_tdn2": b["predicted_tdn2"],
             "gpt_conf": float(b["confianza"] or 0), "origen_texto": origen.get(r["sha256"], "sin_texto"),
             "procedencia": r["rel_path"].split("/")[-1].lower() in proced}
        for nombre, m in modelos.items():
            if r["sha256"] in pos:
                t0 = time.perf_counter()
                p = m.predecir(X[[pos[r["sha256"]]]])[0]
                lat.setdefault(nombre, []).append(time.perf_counter() - t0)
                c = float(cal[nombre].aplicar([p.conf_tdn1])[0])
            else:  # sin texto: cuenta como fallo del clasificador nuevo
                p, c = Prediccion("", "", 0.0), 0.0
            f[f"{nombre}_tdn1"], f[f"{nombre}_tdn2"], f[f"{nombre}_conf"] = p.tdn1, p.tdn2, c
            f[f"H{nombre}_tdn1"], f[f"H{nombre}_tdn2"], f[f"H{nombre}_modelo"] = hibrido(
                p, c, umbral[nombre], f["gpt_tdn1"], f["gpt_tdn2"])
        filas.append(f)

    clave = {m: {"umbral": umbral[m]} for m in modelos}
    res = {"golden_evaluados": len(filas), "golden_inventario": len(golden),
           "todas": metricas_vista(filas, clave),
           "sin_procedencia": metricas_vista([f for f in filas if not f["procedencia"]], clave),
           "latencia_p95_s": {m: float(np.percentile(v, 95)) for m, v in lat.items()},
           "origen_texto_golden": {o: sum(f["origen_texto"] == o for f in filas) for o in {f["origen_texto"] for f in filas}}}
    salida = EVAL / "runs" / f"{datetime.now():%Y%m%d-%H%M%S}-clasificador-embeddings"
    salida.mkdir(parents=True)
    with (salida / "resultados.csv").open("w", newline="", encoding="utf-8-sig") as fh:
        w = csv.DictWriter(fh, list(filas[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(filas)
    (salida / "metricas.json").write_text(json.dumps(res, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(res, indent=2, ensure_ascii=False))
    print("escrito en", salida)


if __name__ == "__main__":
    main()
