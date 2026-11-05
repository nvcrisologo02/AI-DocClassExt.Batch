"""Puerta de calidad del clasificador por embeddings: v1 frente a v2 sobre cal y hold2 (AB#100780).

Uso:
  .venv/Scripts/python puerta_calidad.py --v1 out/clasificador-embeddings-v1.json \
      --v2 out/clasificador-embeddings-v2.json --inventario <ruta>

Pasa si el acierto TDN1 de v2 en cal no baja el de v1 y en hold2 llega al mínimo. Código de salida 0 si pasa, 1 si falla.
"""
import argparse
import json
from pathlib import Path

import numpy as np

from metricas import acierto, cobertura_y_acierto, ece


def _softmax(z: np.ndarray) -> np.ndarray:
    z = z - z.max(axis=1, keepdims=True)
    e = np.exp(z)
    return e / e.sum(axis=1, keepdims=True)


def _tdn2(m2: dict, x: np.ndarray) -> str:
    """Misma regla que ClasificadorLR.predecir: constante, sigmoide si binaria, argmax si multiclase."""
    if "constante" in m2:
        return m2["constante"]
    W, b, clases = np.array(m2["coef"], float), np.array(m2["intercept"], float), m2["clases"]
    z = W @ x + b
    if len(W) == 1:
        return clases[1] if 1.0 / (1.0 + np.exp(-z[0])) >= 0.5 else clases[0]
    return clases[int(np.argmax(z))]


def predecir_artefacto(doc: dict, X: np.ndarray) -> tuple[list[str], list[str], np.ndarray]:
    """TDN1, TDN2 y confianza (máximo del softmax TDN1) a partir del artefacto exportado."""
    t = doc["tdn1"]
    P = _softmax(X @ np.array(t["coef"], float).T + np.array(t["intercept"], float))
    j = P.argmax(axis=1)
    p1 = [t["clases"][i] for i in j]
    p2 = [_tdn2(doc["tdn2"][f], x) if f in doc["tdn2"] else "" for f, x in zip(p1, X)]
    return p1, p2, P[np.arange(len(P)), j]


def evaluar(doc: dict, X: np.ndarray, t1: list[str], t2: list[str]) -> dict:
    p1, p2, conf = predecir_artefacto(doc, X)
    ok1 = np.array([a == b for a, b in zip(p1, t1)])
    cob, aci = cobertura_y_acierto(conf, ok1, 0.6)
    return {
        "tdn1": acierto(t1, p1),
        "tdn2": acierto(t2, p2),
        "ece": ece(conf, ok1),
        "cobertura_06": cob,
        "acierto_06": aci,
        "n": len(t1),
    }


def veredicto(cal_anterior: float, cal_nuevo: float, hold_nuevo: float, minimo_hold: float = 0.80,
              n_hold: int | None = None, holdout: str = "hold2") -> tuple[bool, list[str]]:
    motivos = []
    if cal_nuevo < cal_anterior:
        motivos.append(f"cal: nuevo {cal_nuevo:.1%} por debajo del anterior {cal_anterior:.1%}")
    if n_hold == 0:
        motivos.append(f"{holdout}: sin documentos en el inventario")
    elif hold_nuevo < minimo_hold:
        motivos.append(f"{holdout}: nuevo {hold_nuevo:.1%} por debajo del mínimo {minimo_hold:.1%}")
    return not motivos, motivos


def _particion(inv: dict, shas: list[str], X: np.ndarray, nombre: str):
    pos = {h: i for i, h in enumerate(shas)}
    filas = [r for r in inv.values() if r["particion"] == nombre]
    con = [r for r in filas if r["sha256"] in pos]
    Xp = X[[pos[r["sha256"]] for r in con]] if con else X[:0]
    return Xp, [r["tdn1"] for r in con], [r["tdn2"] for r in con], len(filas) - len(con)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--anterior", "--v1", dest="anterior", required=True, help="Artefacto JSON de la versión vigente")
    ap.add_argument("--nuevo", "--v2", dest="nuevo", required=True, help="Artefacto JSON de la versión candidata")
    ap.add_argument("--inventario", default=None, help="CSV del inventario (por defecto CACHE/inventario.csv)")
    ap.add_argument("--holdout", default="hold2", help="Partición del holdout del corte nuevo (v2: hold2, v3: hold3)")
    ap.add_argument("--salida", default=None, help="JSON de resultados (por defecto out/puerta-calidad-<version nueva>.json)")
    args = ap.parse_args()

    from comun import CACHE
    from embeddings import cargar
    from exportar_modelo import leer_inventario

    anterior = json.loads(Path(args.anterior).read_text(encoding="utf-8"))
    nuevo = json.loads(Path(args.nuevo).read_text(encoding="utf-8"))
    version_nueva = nuevo.get("manifiesto", {}).get("version", "nuevo")
    shas, X = cargar()
    inv = leer_inventario(Path(args.inventario) if args.inventario else CACHE / "inventario.csv")

    partes = list(dict.fromkeys(["cal", "hold2", args.holdout]))
    res: dict = {"anterior": anterior.get("manifiesto", {}).get("version"), "nuevo": version_nueva,
                 "holdout": args.holdout, "sin_vector": {}}
    datos = {}
    for part in partes:
        Xp, a, b, falta = _particion(inv, shas, X, part)
        datos[part] = (Xp, a, b)
        res["sin_vector"][part] = falta
        res[part] = {"anterior": evaluar(anterior, Xp, a, b), "nuevo": evaluar(nuevo, Xp, a, b)}

    print(f"{'partición':<10}{'modelo':<10}{'n':>6}{'TDN1':>8}{'TDN2':>8}{'ECE':>8}{'cob@0,6':>9}{'aci@0,6':>9}")
    for part in partes:
        for v in ("anterior", "nuevo"):
            r = res[part][v]
            print(f"{part:<10}{v:<10}{r['n']:>6}{r['tdn1']:>8.1%}{r['tdn2']:>8.1%}{r['ece']:>8.3f}"
                  f"{r['cobertura_06']:>9.1%}{r['acierto_06']:>9.1%}")

    Xh, a, b = datos[args.holdout]
    p1, _, _ = predecir_artefacto(nuevo, Xh)
    por_tdn2: dict[str, dict] = {}
    for t2_, e, p in zip(b, a, p1):
        d = por_tdn2.setdefault(t2_ or "(vacío)", {"n": 0, "ok": 0})
        d["n"] += 1
        d["ok"] += e == p
    clave = f"{args.holdout}_nuevo_por_tdn2"
    res[clave] = {k: {"n": d["n"], "tdn1": d["ok"] / d["n"]} for k, d in sorted(por_tdn2.items())}
    print(f"\nnuevo en {args.holdout} por tdn2")
    print(f"{'tdn2':<24}{'n':>6}{'TDN1':>8}")
    for k, d in res[clave].items():
        print(f"{k:<24}{d['n']:>6}{d['tdn1']:>8.1%}")

    ok, motivos = veredicto(res["cal"]["anterior"]["tdn1"], res["cal"]["nuevo"]["tdn1"],
                            res[args.holdout]["nuevo"]["tdn1"], n_hold=res[args.holdout]["nuevo"]["n"],
                            holdout=args.holdout)
    res["pasa"], res["motivos"] = ok, motivos
    salida = Path(args.salida) if args.salida else Path("out") / f"puerta-calidad-{version_nueva}.json"
    salida.parent.mkdir(parents=True, exist_ok=True)
    salida.write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PUERTA: PASA" if ok else f"PUERTA: FALLA ({'; '.join(motivos)})")
    raise SystemExit(0 if ok else 1)


if __name__ == "__main__":
    main()
