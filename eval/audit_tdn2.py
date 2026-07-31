"""Auditor de subtipos TDN2 (AB#99973) — para los casos REVISAR-TDN2 (familia
correcta, subtipo distinto).

Cada confusion de subtipo se clasifica en:
  CATALOG_DUP    las descripciones de ambos subtipos son casi identicas -> defecto
                 de catalogo (subtipos redundantes), no arreglable por clasificador.
  CATCHALL       uno de los subtipos es un cajon de sastre ("otro/otra/no
                 contemplado/resto") -> distincion inherentemente ambigua.
  MISLABEL_LIKELY   el contenido encaja mas con el subtipo PREDICHO -> re-etiquetar.
  REAL_ERROR_LIKELY el contenido encaja mas con el subtipo ESPERADO -> target prompt.
  AMBIGUOUS      encaje similar con ambos.
  UNDECIDABLE    sin senal (poco texto o descripciones sin keywords utiles).
  NO_TEXT        escaneado sin texto extraible por pypdf (usar audit_notext_db).

Metodo: en vez de marcadores regex (las descripciones de subtipo son frases
cortas), puntua el texto del documento por solape de palabras clave de cada
descripcion de subtipo. Determinista, sin LLM.

Uso: python eval/audit_tdn2.py --run eval/runs/<dir>
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import unicodedata
from collections import Counter
from difflib import SequenceMatcher
from pathlib import Path

from pypdf import PdfReader

CATCHALL_RE = re.compile(r"\botr[oa]s?\b|no (contemplad|incluid)|no est[aá] contemplad|resto de|dem[aá]s", re.IGNORECASE)

STOP = set("""de la el en y a los las del un una por con para que se su sus o segun sobre como al
es este esta documento mediante traves cual cuales referente respecto aquellos aquellas presente
clasificacion tipo tipologia forma parte otro otra otros otras haber sido""".split())


def strip_accents(s: str) -> str:
    return "".join(c for c in unicodedata.normalize("NFD", s) if unicodedata.category(c) != "Mn")


def keywords(desc: str) -> set[str]:
    words = re.findall(r"[a-zA-Zñáéíóú]{4,}", strip_accents(desc).lower())
    return {w for w in words if w not in STOP}


def load_tdn2(path: Path) -> dict[str, dict]:
    cat = json.loads(path.read_text(encoding="utf-8-sig"))
    out = {}
    for x in cat:
        code = x.get("Codigo") or x.get("CodigoTdn2")
        if code:
            out[code] = {"nombre": x.get("Nombre", ""), "desc": x.get("Descripcion") or ""}
    return out


def first_pages_text(pdf: Path, pages: int) -> str:
    r = PdfReader(str(pdf))
    return " ".join(r.pages[i].extract_text() or "" for i in range(min(pages, len(r.pages))))


def classify(exp: str, pred: str, cat: dict[str, dict], text: str) -> tuple[str, str]:
    de, dp = cat.get(exp, {}), cat.get(pred, {})
    desc_e, desc_p = de.get("desc", ""), dp.get("desc", "")
    ne, npr = de.get("nombre", ""), dp.get("nombre", "")

    # 1) subtipos duplicados: descripciones casi identicas
    norm_e, norm_p = strip_accents(desc_e.lower()).strip(), strip_accents(desc_p.lower()).strip()
    if desc_e and desc_p and SequenceMatcher(None, norm_e, norm_p).ratio() >= 0.90:
        return "CATALOG_DUP", "descripciones de subtipo casi identicas"

    # 2) cajon de sastre
    if CATCHALL_RE.search(ne + " " + desc_e) or CATCHALL_RE.search(npr + " " + desc_p):
        catch = exp if CATCHALL_RE.search(ne + " " + desc_e) else pred
        return "CATCHALL", f"{catch} es cajon de sastre ('otro/no contemplado')"

    # 3) solape de keywords con el texto del documento
    if len(text.strip()) < 80:
        return "NO_TEXT", "escaneado sin texto (pypdf)"
    ke = keywords(ne + " " + desc_e) - keywords(npr + " " + desc_p)  # keywords distintivas de cada uno
    kp = keywords(npr + " " + desc_p) - keywords(ne + " " + desc_e)
    tl = strip_accents(text.lower())
    se = sum(1 for w in ke if w in tl)
    sp = sum(1 for w in kp if w in tl)
    if not ke and not kp:
        return "UNDECIDABLE", "sin keywords distintivas entre subtipos"
    if sp > se and sp > 0:
        return "MISLABEL_LIKELY", f"texto encaja con {pred} (kw {sp} vs {se})"
    if se > sp and se > 0:
        return "REAL_ERROR_LIKELY", f"texto encaja con {exp} (kw {se} vs {sp})"
    return "AMBIGUOUS", f"encaje similar (kw {se} vs {sp})"


RANK = {"REAL_ERROR_LIKELY": 0, "MISLABEL_LIKELY": 1, "CATALOG_DUP": 2, "CATCHALL": 3,
        "AMBIGUOUS": 4, "UNDECIDABLE": 5, "NO_TEXT": 6}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", required=True)
    ap.add_argument("--catalog", default=str(Path(__file__).with_name("catalogotdn2.json")))
    ap.add_argument("--corpus-root", default=r"H:\Documentia\ParaNacho\Class")
    ap.add_argument("--pages", type=int, default=5)
    args = ap.parse_args()
    run = Path(args.run)

    informe = [r for r in csv.DictReader((run / "informe_por_fichero.csv").open(encoding="utf-8-sig"), delimiter=";")
               if r["resultado"] == "REVISAR-TDN2"]
    # necesitamos rel_path: lo tomamos de results.csv por filename
    results = {r["file_name"]: r for r in csv.DictReader((run / "results.csv").open(encoding="utf-8-sig"), delimiter=";")}
    cat = load_tdn2(Path(args.catalog))

    out = []
    summ: Counter = Counter()
    for r in informe:
        res = results.get(r["filename"])
        rel = res["rel_path"] if res else ""
        exp, pred = r["tdn2_esperado"], r["tdn2_clasificado"]
        text = ""
        if rel:
            try:
                text = first_pages_text(Path(args.corpus_root) / rel.replace("/", "\\"), args.pages)
            except Exception:
                text = ""
        v, motivo = classify(exp, pred, cat, text)
        summ[v] += 1
        out.append({"filename": r["filename"], "nombre": r["nombre"], "tdn1": r["tdn1_esperado"],
                    "tdn2_esperado": exp, "tdn2_clasificado": pred, "veredicto_tdn2": v, "motivo": motivo})

    out.sort(key=lambda x: (RANK.get(x["veredicto_tdn2"], 9), x["tdn1"], x["tdn2_esperado"]))
    out_csv = run / "audit_tdn2.csv"
    with out_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(out)

    total = len(out)
    print(f"REVISAR-TDN2 auditados: {total} -> {out_csv}\n")
    for k in ["REAL_ERROR_LIKELY", "MISLABEL_LIKELY", "CATALOG_DUP", "CATCHALL", "AMBIGUOUS", "UNDECIDABLE", "NO_TEXT"]:
        if summ.get(k):
            print(f"  {k:18s} {summ[k]:3d}  ({100*summ[k]/total:.0f}%)")


if __name__ == "__main__":
    main()
