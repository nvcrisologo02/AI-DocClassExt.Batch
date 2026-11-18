"""Puerta de calidad del clasificador por embeddings: versión anterior frente a candidata sobre cal, hold2 y el
holdout del corte nuevo (AB#100780 v2, AB#101313 v3, AB#101418 cal ampliada).

Uso:
  .venv/Scripts/python puerta_calidad.py --anterior out/clasificador-embeddings-v3.json \
      --nuevo out/clasificador-embeddings-v4.json --inventario <ruta> --holdout hold4 \
      --cal-ampliada --salida out/puerta-calidad-v4.json [--tolerancia-cal 0.005]
(--v1/--v2 siguen admitidos como alias de --anterior/--nuevo.)

Pasa si el acierto TDN1 del nuevo en cal no baja más de --tolerancia-cal (0,5 pp) frente al anterior y en el
holdout llega al mínimo (0,80). Con --cal-ampliada (decisión del usuario, 2026-11-18, AB#101418) la condición de
cal se mide sobre cal más los holdouts de los cortes ANTERIORES (hold2, hold3, ...), nunca el holdout evaluado:
el modelo anterior no vio esas clases y meterlo haría pasar cualquier versión. cal original se imprime siempre
como serie histórica. Sin el flag la salida es la de siempre (§13 y §14 del INFORME siguen reproducibles).
Código de salida 0 si pasa, 1 si falla.
"""
import argparse
import json
import re
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
              n_hold: int | None = None, holdout: str = "hold2",
              tolerancia_cal: float = 0.005, etiqueta_cal: str = "cal") -> tuple[bool, list[str]]:
    """cal no puede bajar más de tolerancia_cal (0,5 pp por defecto, decisión del 2026-11-05 en v3) y el
    holdout nuevo debe llegar a minimo_hold."""
    motivos = []
    if cal_nuevo < cal_anterior - tolerancia_cal:
        motivos.append(f"{etiqueta_cal}: nuevo {cal_nuevo:.1%} por debajo del anterior {cal_anterior:.1%} "
                       f"en más de {tolerancia_cal:.1%}")
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


def _numero_holdout(nombre: str) -> int | None:
    m = re.fullmatch(r"hold(\d+)", nombre)
    return int(m.group(1)) if m else None


def particiones_cal_ampliada(inv: dict, holdout: str) -> list[str]:
    """cal más los holdouts de cortes ANTERIORES al evaluado (hold<k> con k < n), ordenados por número.
    El holdout evaluado y los posteriores quedan fuera: el modelo anterior no vio esas clases y meterlos
    haría pasar cualquier versión."""
    n_eval = _numero_holdout(holdout)
    if n_eval is None:
        raise ValueError(f"--holdout debe tener el formato hold<n> para usar --cal-ampliada: {holdout!r}")
    numeros = {k for k in (_numero_holdout(r["particion"]) for r in inv.values()) if k is not None and k < n_eval}
    return ["cal"] + [f"hold{k}" for k in sorted(numeros)]


def holdouts_posteriores(inv: dict, holdout: str) -> list[str]:
    """Holdouts del inventario con número mayor que el evaluado: señal de que --holdout no es el del último corte."""
    n_eval = _numero_holdout(holdout)
    numeros = {k for k in (_numero_holdout(r["particion"]) for r in inv.values()) if k is not None and k > (n_eval or 0)}
    return [f"hold{k}" for k in sorted(numeros)]


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--anterior", "--v1", dest="anterior", required=True, help="Artefacto JSON de la versión vigente")
    ap.add_argument("--nuevo", "--v2", dest="nuevo", required=True, help="Artefacto JSON de la versión candidata")
    ap.add_argument("--inventario", default=None, help="CSV del inventario (por defecto CACHE/inventario.csv)")
    ap.add_argument("--holdout", default="hold2", help="Partición del holdout del corte nuevo (v2: hold2, v3: hold3)")
    ap.add_argument("--tolerancia-cal", type=float, default=0.005,
                    help="Caída máxima admitida del TDN1 en cal frente al anterior (fracción; 0.005 = 0,5 pp)")
    ap.add_argument("--cal-ampliada", action="store_true",
                    help="La condición de cal se mide sobre cal + holdouts de cortes anteriores (excluye --holdout)")
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

    ampliada = particiones_cal_ampliada(inv, args.holdout) if args.cal_ampliada else ["cal"]
    posteriores = holdouts_posteriores(inv, args.holdout) if args.cal_ampliada else []
    if posteriores:
        print(f"AVISO: el inventario tiene holdouts posteriores a --holdout {args.holdout}: {posteriores}. "
              "Quedan fuera de la cal ampliada; comprueba que --holdout es el del corte evaluado.")
    partes = list(dict.fromkeys(ampliada + ["hold2", args.holdout]))
    res: dict = {"anterior": anterior.get("manifiesto", {}).get("version"), "nuevo": version_nueva,
                 "holdout": args.holdout, "condicion_cal": "cal_ampliada" if args.cal_ampliada else "cal",
                 "sin_vector": {}}
    datos = {}
    for part in partes:
        Xp, a, b, falta = _particion(inv, shas, X, part)
        datos[part] = (Xp, a, b)
        res["sin_vector"][part] = falta
        res[part] = {"anterior": evaluar(anterior, Xp, a, b), "nuevo": evaluar(nuevo, Xp, a, b)}
    filas = list(partes)
    if args.cal_ampliada:
        Xa = np.vstack([datos[p][0] for p in ampliada])
        a = [t for p in ampliada for t in datos[p][1]]
        b = [t for p in ampliada for t in datos[p][2]]
        res["cal_ampliada"] = {"particiones": ampliada, "anterior": evaluar(anterior, Xa, a, b),
                               "nuevo": evaluar(nuevo, Xa, a, b)}
        filas.insert(1, "cal_ampliada")
    cal_cond = "cal_ampliada" if args.cal_ampliada else "cal"

    print(f"{'partición':<10}{'modelo':<10}{'n':>6}{'TDN1':>8}{'TDN2':>8}{'ECE':>8}{'cob@0,6':>9}{'aci@0,6':>9}")
    for part in filas:
        for v in ("anterior", "nuevo"):
            r = res[part][v]
            print(f"{('cal_ampl.' if part == 'cal_ampliada' else part):<10}{v:<10}{r['n']:>6}{r['tdn1']:>8.1%}{r['tdn2']:>8.1%}{r['ece']:>8.3f}"
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

    ok, motivos = veredicto(res[cal_cond]["anterior"]["tdn1"], res[cal_cond]["nuevo"]["tdn1"],
                            res[args.holdout]["nuevo"]["tdn1"], n_hold=res[args.holdout]["nuevo"]["n"],
                            holdout=args.holdout, tolerancia_cal=args.tolerancia_cal, etiqueta_cal=cal_cond)
    res["tolerancia_cal"] = args.tolerancia_cal
    res["pasa"], res["motivos"] = ok, motivos
    salida = Path(args.salida) if args.salida else Path("out") / f"puerta-calidad-{version_nueva}.json"
    salida.parent.mkdir(parents=True, exist_ok=True)
    salida.write_text(json.dumps(res, ensure_ascii=False, indent=2), encoding="utf-8")
    print("PUERTA: PASA" if ok else f"PUERTA: FALLA ({'; '.join(motivos)})")
    raise SystemExit(0 if ok else 1)


if __name__ == "__main__":
    main()
