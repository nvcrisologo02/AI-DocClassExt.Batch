"""Regenera las cifras derivadas de INFORME.md que no están en los metricas.json.

Solo lee ficheros locales (sin llamadas a Azure ni a BD):
- resultados.csv y metricas.json del run de A/B/híbridos (tarea 7) y del run de C (tarea 8),
- CACHE/inventario.csv, CACHE/embeddings.npz y CACHE/embeddings_uso.csv.

Imprime, por apartado del informe:
- §1  McNemar de los híbridos y de C frente al GPT baseline, C en la vista sin_procedencia
      (mismo criterio de procedencia que evaluar.py: la columna `procedencia` del run de T7)
      y el acierto de A en los tramos de confianza (máximo alcanzable en el golden).
- §3  diagnóstico cal/golden: acierto de A y del 1-NN, similitud máxima a train y fracción
      con vecino >= 0,98; cobertura y acierto de A en cal (dentro de muestra) con su umbral.
- §4  fiabilidad de A, cobertura frente a acierto por umbral, 10 pares de confusión TDN1
      de A (con los mismos errores del GPT) y documentos del golden de familias excluidas.
- §5  tokens y latencia de embeddings, y la relación de coste frente a GPT con los precios
      de la Azure Retail Prices API y el coste medio de clasificación que devuelve
      coste_gpt_golden.sql (constantes documentadas abajo; el script no consulta la BD).
- §6  acierto por origen del texto en el golden y reparto de origen en el corpus.
- §7  fracción de C con confianza 1,0.

Nunca imprime texto de documentos ni rutas de fichero: solo etiquetas y cifras.
Uso: python analisis_informe.py [--run-ab DIR] [--run-c DIR]"""
from __future__ import annotations

import argparse
import collections
import csv
import json
from pathlib import Path

import numpy as np

from comun import CACHE, EVAL
from embeddings import cargar
from metricas import cobertura_y_acierto, ece, mcnemar, umbral_para_acierto
from modelos import ClasificadorLR, calibrar

RUN_AB = EVAL / "runs" / "20260928-180316-clasificador-embeddings"
RUN_C = EVAL / "runs" / "20260928-175630-logprobs-gpt"

# Precios de la Azure Retail Prices API (https://prices.azure.com/api/retail/prices),
# consultados el 2026-09-28 con armRegionName eq 'westeurope'. El deployment de embeddings
# (text-embedding-3-large-010650) es GlobalStandard, así que el medidor es el "glbl".
PRECIO_EMB_USD_1K = 0.00013   # meterName 'text-embedding-3-large-glbl Tokens', USD
PRECIO_EMB_EUR_1K = 0.0001    # mismo medidor con currencyCode=EUR (la API lo redondea)
# Salida de coste_gpt_golden.sql contra DEV (srbsqldevdocai / DocumentIA) el 2026-09-28.
COSTE_GPT_EUR_MEDIO = 0.005742
COSTE_GPT_EUR_MEDIANA = 0.004597

UMBRALES = (0.5, 0.6, 0.7, 0.8, 0.9, 0.95)


def leer(p: Path):
    return list(csv.DictReader(p.open(encoding="utf-8-sig"), delimiter=";"))


def pct(x) -> str:
    return f"{100 * float(x):.2f} %"


def diagnostico_cal_golden():
    """§3: reentrena A con la misma partición que evaluar.py y compara cal con golden."""
    inv = {r["sha256"]: r for r in leer(CACHE / "inventario.csv")}
    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}

    def conjunto(n):
        hs = [h for h, r in inv.items() if r["particion"] == n and h in pos]
        return X[[pos[h] for h in hs]], [inv[h]["tdn1"] for h in hs], [inv[h]["tdn2"] for h in hs]

    X_tr, t1, t2 = conjunto("train")
    X_ca, c1, _ = conjunto("cal")
    X_go, g1, _ = conjunto("golden")
    m = ClasificadorLR().fit(X_tr, t1, t2)
    print("\n== §3 diagnóstico cal / golden (A reentrenado con train)")
    for nombre, Xs, ys in (("cal", X_ca, c1), ("golden", X_go, g1)):
        p = m.predecir(Xs)
        acc = np.mean([a.tdn1 == y for a, y in zip(p, ys)])
        S = Xs @ X_tr.T
        mx = S.max(1)
        nn = np.mean([t1[j] == y for j, y in zip(S.argmax(1), ys)])
        print(f"{nombre}: n={len(ys)} acierto_A={acc:.3f} acierto_1NN={nn:.3f} "
              f"sim_max p50={np.median(mx):.3f} p90={np.percentile(mx, 90):.3f} "
              f"frac>=0,98={np.mean(mx >= 0.98):.3f}")
    cal = calibrar(m, X_ca, c1)
    p = m.predecir(X_ca)
    conf = cal.aplicar([a.conf_tdn1 for a in p])
    ok = [a.tdn1 == y for a, y in zip(p, c1)]
    t = umbral_para_acierto(conf, ok, 0.90)
    cob, acc = cobertura_y_acierto(conf, ok, t)
    print(f"A en cal (dentro de muestra): umbral={t} cobertura={pct(cob)} acierto={pct(acc)}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--run-ab", type=Path, default=RUN_AB)
    ap.add_argument("--run-c", type=Path, default=RUN_C)
    ap.add_argument("--sin-diagnostico", action="store_true", help="omite el reentrenamiento de §3")
    a = ap.parse_args()

    f7 = leer(a.run_ab / "resultados.csv")
    m7 = json.loads((a.run_ab / "metricas.json").read_text(encoding="utf-8"))
    f8 = leer(a.run_c / "resultados.csv")
    proc = {f["rel_path"].lower(): f["procedencia"] == "True" for f in f7}
    base = {f["rel_path"].lower(): f for f in f7}
    vistas7 = {"todas": f7, "sin_procedencia": [f for f in f7 if f["procedencia"] == "False"]}
    vistas8 = {"todas": f8, "sin_procedencia": [f for f in f8 if not proc[f["rel_path"].lower()]]}

    print("== §1 aciertos y McNemar frente a GPT (b = acierta el primero y falla GPT)")
    for v, s in vistas7.items():
        ok_gpt = [f["gpt_tdn1"] == f["exp_tdn1"] for f in s]
        print(f"[{v}] n={len(s)} " + " ".join(
            f"{m}={pct(np.mean([f[m + '_tdn1'] == f['exp_tdn1'] for f in s]))}" for m in ("gpt", "A", "B", "HA", "HB")))
        for m in ("HA", "HB"):
            b, c, p = mcnemar([f[m + "_tdn1"] == f["exp_tdn1"] for f in s], ok_gpt)
            print(f"  {m}: b={b} c={c} p={p:.3f}")
    for v, s in vistas8.items():
        ok = [f["C_tdn1"] == f["exp_tdn1"] for f in s]
        conf = [float(f["C_conf"]) for f in s]
        ok_gpt = [base[f["rel_path"].lower()]["gpt_tdn1"] == f["exp_tdn1"] for f in s]
        b, c, p = mcnemar(ok, ok_gpt)
        print(f"  C [{v}] n={len(s)} tdn1={pct(np.mean(ok))} ece_bruta={ece(conf, ok):.3f} "
              f"McNemar b={b} c={c} p={p:.3f} frac_conf_1,0={np.mean(np.array(conf) >= 0.999):.3f}")

    c = np.array([float(f["A_conf"]) for f in f7])
    ok = np.array([f["A_tdn1"] == f["exp_tdn1"] for f in f7])
    print("\n== §4 fiabilidad de A en el golden (todas)")
    for i in range(10):
        lo, hi = i / 10, (i + 1) / 10
        m = (c >= lo) & ((c < hi) if i < 9 else (c <= hi))
        if m.sum():
            print(f"{lo:.1f}-{hi:.1f} n={m.sum()} conf_media={c[m].mean():.3f} acierto={ok[m].mean():.3f}")
    print("cobertura frente a acierto (descriptivo, umbral sobre el golden)")
    for t in UMBRALES:
        m = c >= t
        print(f"umbral {t:.2f}: cobertura={pct(m.mean())} acierto={pct(ok[m].mean()) if m.sum() else '-'}")
    for v, s in vistas7.items():
        for mod in ("A", "B"):
            cc = np.array([float(f[mod + "_conf"]) for f in s])
            oo = np.array([f[mod + "_tdn1"] == f["exp_tdn1"] for f in s])
            mejor = max((oo[cc >= t].mean() for t in np.unique(cc) if (cc >= t).sum() >= 10), default=0)
            print(f"  [{v}] {mod}: acierto máximo con cualquier umbral (>= 10 docs) = {pct(mejor)}")

    conf_a = collections.Counter((f["exp_tdn1"], f["A_tdn1"] or "(vacío)") for f in f7 if f["A_tdn1"] != f["exp_tdn1"])
    conf_g = collections.Counter((f["exp_tdn1"], f["gpt_tdn1"]) for f in f7 if f["gpt_tdn1"] != f["exp_tdn1"])
    print(f"pares de confusión TDN1 de A (errores totales {sum(conf_a.values())}); empates al final:")
    top = conf_a.most_common()
    corte = top[9][1] if len(top) >= 10 else 0
    for (e, p), n in top:
        if n < corte:
            break
        print(f"  {e} -> {p}: A={n} GPT={conf_g[(e, p)]}")
    excl = set(m7["familias_excluidas"]["lista"])
    s = [f for f in f7 if f["exp_tdn1"] in excl]
    print(f"familias excluidas ({len(excl)}): golden {len(s)} docs "
          f"{dict(collections.Counter(f['exp_tdn1'] for f in s))}; aciertos A={sum(f['A_tdn1'] == f['exp_tdn1'] for f in s)} "
          f"B={sum(f['B_tdn1'] == f['exp_tdn1'] for f in s)}")

    uso = [ln.strip().split(";") for ln in (CACHE / "embeddings_uso.csv").open(encoding="utf-8-sig") if ln.strip()]
    tam = [int(u[0]) for u in uso]
    tok = [int(u[1]) for u in uso]
    seg = np.array([float(u[2]) for u in uso])
    por_doc = sum(tok) / sum(tam)
    print("\n== §5 embeddings: coste y latencia")
    print(f"llamadas={len(uso)} textos={sum(tam)} lotes={dict(collections.Counter(tam))} tokens={sum(tok)} "
          f"tokens/doc={por_doc:.1f}")
    print(f"coste total={sum(tok) / 1000 * PRECIO_EMB_USD_1K:.3f} USD; por doc={por_doc / 1000 * PRECIO_EMB_USD_1K:.6f} USD "
          f"= {por_doc / 1000 * PRECIO_EMB_EUR_1K:.6f} EUR")
    r_eur = por_doc / 1000 * PRECIO_EMB_EUR_1K / COSTE_GPT_EUR_MEDIO
    r_usd = por_doc / 1000 * PRECIO_EMB_USD_1K / COSTE_GPT_EUR_MEDIO
    r_med = por_doc / 1000 * PRECIO_EMB_USD_1K / COSTE_GPT_EUR_MEDIANA
    print(f"relación frente a GPT: {pct(r_eur)} (EUR) | {pct(r_usd)} (USD tratado como EUR) | {pct(r_med)} (frente a la mediana)")
    lat = m7["latencia_p95_s"]
    p95 = np.percentile(seg, 95)
    print(f"segundos por llamada p50={np.median(seg):.3f} p95={p95:.3f} max={seg.max():.3f}; "
          f"p95 extremo a extremo A={p95 + lat['A']:.3f} B={p95 + lat['B']:.3f}")

    print("\n== §6 acierto TDN1 por origen del texto (golden)")
    for o in sorted({f["origen_texto"] for f in f7}):
        s = [f for f in f7 if f["origen_texto"] == o]
        print(f"{o}: n={len(s)} " + " ".join(
            f"{m}={pct(np.mean([f[m + '_tdn1'] == f['exp_tdn1'] for f in s]))}" for m in ("A", "B", "gpt")))

    corpus = collections.Counter(r["origen_texto"] for r in leer(CACHE / "texto_origen.csv"))
    print(f"corpus completo (texto_origen.csv, {sum(corpus.values())} hashes): {dict(corpus)}")

    if not a.sin_diagnostico:
        diagnostico_cal_golden()


if __name__ == "__main__":
    main()
