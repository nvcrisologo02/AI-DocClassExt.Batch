"""Exporta el clasificador A (train) a JSON para la inferencia en C# (AB#100779).

Uso:
  .venv/Scripts/python exportar_modelo.py --tipologias tipologias-dev.json --salida out --version v1
  .venv/Scripts/python exportar_modelo.py --fixture-tests out-fixture

`tipologias-dev.json` se obtiene con sql/tipologias-pares-tdn.sql contra la BD de DEV.
"""
import argparse
import csv
import datetime as dt
import json
from pathlib import Path

import numpy as np
from sklearn.linear_model import LogisticRegression

from modelos import ClasificadorLR

MODELO_EMBEDDINGS = "text-embedding-3-large"
MAX_CHARS = 24000


def _lineal(m: LogisticRegression) -> dict:
    return {
        "clases": [str(c) for c in m.classes_],
        "coef": m.coef_.astype(float).tolist(),
        "intercept": m.intercept_.astype(float).tolist(),
    }


def exportar_modelo(A: ClasificadorLR, manifiesto: dict) -> dict:
    tdn2 = {}
    for fam, m2 in A.m2.items():
        tdn2[fam] = {"constante": m2} if isinstance(m2, str) else _lineal(m2)
    return {"manifiesto": manifiesto, "tdn1": _lineal(A.m1), "tdn2": tdn2}


def _subtipos(A: ClasificadorLR, fam: str) -> set[str]:
    m2 = A.m2.get(fam, "")
    return {m2.upper()} if isinstance(m2, str) else {str(c).upper() for c in m2.classes_}


def tipologias_cubiertas(A: ClasificadorLR, tipologias: list[dict]) -> list[dict]:
    """Tipologias del catalogo cuyo par TDN1/TDN2 puede predecir A."""
    fams = {str(c) for c in A.m1.classes_}
    out = []
    for t in tipologias:
        t1 = (t.get("tdn1") or "").strip().upper()
        t2 = (t.get("tdn2") or "").strip().upper()
        if not t1 or not t2 or t1 not in fams or t2 not in _subtipos(A, t1):
            continue
        out.append({"codigo": t["codigo"], "tdn1": t1, "tdn2": t2})
    return out


def paridad(A: ClasificadorLR, X: np.ndarray, shas: list[str], n: int = 10) -> dict:
    """Vectores de entrada con las distribuciones que debe reproducir la inferencia en C#."""
    P = A.m1.predict_proba(X[:n])
    preds = A.predecir(X[:n])
    casos = []
    for i in range(min(n, len(X))):
        fam = preds[i].tdn1
        m2 = A.m2.get(fam, "")
        if isinstance(m2, str):
            p2 = {m2: 1.0}
        else:
            p2 = dict(zip([str(c) for c in m2.classes_], m2.predict_proba(X[i:i + 1])[0].tolist()))
        casos.append({
            "sha256": shas[i],
            "vector": X[i].astype(float).tolist(),
            "probTdn1": dict(zip([str(c) for c in A.m1.classes_], P[i].tolist())),
            "tdn1": fam,
            "tdn2": preds[i].tdn2,
            "probTdn2": p2,
        })
    return {"casos": casos}


def manifiesto(version: str, particion: str, n_docs: int, dims: int, tipologias: list[dict]) -> dict:
    return {
        "version": version,
        "entrenadoEn": dt.date.today().isoformat(),
        "particion": particion,
        "nDocumentos": n_docs,
        "modeloEmbeddings": MODELO_EMBEDDINGS,
        "dimensiones": dims,
        "maxChars": MAX_CHARS,
        "calibrado": False,
        "umbralRecomendado": 0.6,
        "tipologias": tipologias,
    }


def entrenar_train():
    from comun import CACHE
    from embeddings import cargar

    inv = {r["sha256"]: r for r in csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";")}
    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}
    tr = [h for h, r in inv.items() if r["particion"] == "train" and h in pos]
    X_tr = X[[pos[h] for h in tr]]
    A = ClasificadorLR().fit(X_tr, [inv[h]["tdn1"] for h in tr], [inv[h]["tdn2"] for h in tr])
    return A, X_tr, tr


def escribir(ruta: Path, doc: dict) -> None:
    ruta.parent.mkdir(parents=True, exist_ok=True)
    ruta.write_text(json.dumps(doc, ensure_ascii=False), encoding="utf-8")


def construir_fixture_sintetico(salida: Path, semilla: int = 0) -> None:
    """Modelo pequeno (8 dims) con familia multiclase, binaria, constante y una familia sin ningún TDN2 etiquetado (sin modelo TDN2)."""
    rng = np.random.default_rng(semilla)
    centros = {
        "AAAA-01": [5, 0, 0, 0, 0, 0, 0, 0], "AAAA-02": [5, 1, 0, 0, 0, 0, 0, 0], "AAAA-03": [5, 0, 1, 0, 0, 0, 0, 0],
        "BBBB-01": [0, 5, 0, 0, 0, 0, 0, 0], "BBBB-02": [0, 5, 1, 0, 0, 0, 0, 0],
        "CCCC-01": [0, 0, 5, 0, 0, 0, 0, 0],
        "DDDD-01": [0, 0, 0, 5, 0, 0, 0, 0],
    }
    X, t1, t2 = [], [], []
    for i in range(140):
        k = list(centros)[i % len(centros)]
        X.append(np.array(centros[k], float) + rng.normal(0, 0.3, 8))
        t1.append(k[:4])
        t2.append("" if k == "DDDD-01" and i % 14 else k)
    X = np.array(X)
    X /= np.linalg.norm(X, axis=1, keepdims=True)
    A = ClasificadorLR().fit(X, t1, t2)
    cat = [
        {"codigo": "a.01", "tdn1": "AAAA", "tdn2": "AAAA-01"}, {"codigo": "a.02", "tdn1": "AAAA", "tdn2": "AAAA-02"},
        {"codigo": "a.03", "tdn1": "AAAA", "tdn2": "AAAA-03"}, {"codigo": "b.01", "tdn1": "BBBB", "tdn2": "BBBB-01"},
        {"codigo": "b.02", "tdn1": "BBBB", "tdn2": "BBBB-02"}, {"codigo": "c.01", "tdn1": "CCCC", "tdn2": "CCCC-01"},
        {"codigo": "d.01", "tdn1": "DDDD", "tdn2": "DDDD-01"},
    ]
    man = manifiesto("fixture", "sintetico", len(X), 8, tipologias_cubiertas(A, cat))
    escribir(salida / "clasificador-embeddings-fixture.json", exportar_modelo(A, man))
    # 7 casos: indices 0..6 recorren AAAA-01, AAAA-02, AAAA-03, BBBB-01, BBBB-02, CCCC-01 y DDDD-01 (familia sin modelo TDN2)
    escribir(salida / "paridad-fixture.json", paridad(A, X, [f"fixture-{i}" for i in range(len(X))], n=7))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--tipologias", help="JSON [{codigo,tdn1,tdn2}] exportado de la BD de DEV")
    ap.add_argument("--salida", default="out")
    ap.add_argument("--version", default="v1")
    ap.add_argument("--fixture-tests", help="Carpeta donde escribir el modelo sintetico para los tests del backend")
    args = ap.parse_args()

    if args.fixture_tests:
        construir_fixture_sintetico(Path(args.fixture_tests))
        print(f"fixture escrito en {args.fixture_tests}")
        return

    if not args.tipologias:
        ap.error("--tipologias es obligatorio para exportar el modelo real")
    tipologias = json.loads(Path(args.tipologias).read_text(encoding="utf-8-sig"))
    A, X_tr, tr = entrenar_train()
    man = manifiesto(args.version, "train", len(tr), X_tr.shape[1], tipologias_cubiertas(A, tipologias))
    salida = Path(args.salida)
    escribir(salida / f"clasificador-embeddings-{args.version}.json", exportar_modelo(A, man))
    escribir(salida / f"paridad-{args.version}.json", paridad(A, X_tr, tr, n=10))
    print(f"modelo {args.version}: {len(A.m1.classes_)} familias, {len(man['tipologias'])} tipologias cubiertas, "
          f"{len(tr)} documentos; escrito en {salida}")


if __name__ == "__main__":
    main()
