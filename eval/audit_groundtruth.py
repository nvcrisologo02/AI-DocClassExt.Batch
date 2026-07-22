"""Auditor semi-automatico de ground-truth (primer entregable de AB#99973).

Para cada discrepancia clasificador-vs-etiqueta de un run de evaluacion, extrae el
texto de las primeras paginas del documento y comprueba si contiene los marcadores
(autotitulos) de la familia PREDICHA o de la familia ESPERADA, para dictaminar:

  MISLABEL_LIKELY   el contenido respalda al clasificador -> la etiqueta esta mal
                    (re-etiquetar: candidato de alta prioridad para AB#99973)
  REAL_ERROR_LIKELY el contenido respalda la etiqueta -> el clasificador fallo
                    (candidato real de mejora de prompt)
  AMBIGUOUS_BOTH    marcadores de ambas familias presentes -> revision humana
  PROVENANCE        alguna de las familias se define por procedencia/workflow y
                    NO es decidible por contenido (AB#99976)
  UNDECIDABLE       sin marcadores de ninguna -> revision humana

Los marcadores se AUTO-DERIVAN de las descripciones del catalogo TDN1 (frases
entrecomilladas y nombre de la familia), con un diccionario curado de refuerzo para
las familias validadas a mano en el analisis. No usa LLM: es determinista y barato.

Uso:
  python eval/audit_groundtruth.py --run eval/runs/<dir> [--catalog <catalogotdn1.json>] [--pages 5]

Salida en el dir del run: audit_groundtruth.csv (ordenado por veredicto y evidencia).
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import re
from collections import Counter
from pathlib import Path

from pypdf import PdfReader

# Familias definidas por procedencia/workflow (no decidibles por contenido) — AB#99976.
PROVENANCE = {"PRPI", "ACUI", "ACUE", "PRPE", "DEAC", "SERE"}

# Refuerzo curado de marcadores para familias validadas a mano (autotitulos reales
# observados en los PDFs del corpus). Complementan a los auto-derivados del catalogo.
CURATED_MARKERS: dict[str, list[str]] = {
    "NOTS": [r"nota simple", r"informaci[oó]n registral"],
    "CERJ": [r"certific[oa]", r"certificaci[oó]n"],
    "CERA": [r"justificante de (pago|transferencia)", r"recib[ií]", r"carta de pago"],
    "INRG": [r"calificaci[oó]n registral", r"inscripci[oó]n practicada", r"asiento de presentaci[oó]n"],
    "ESCR": [r"escritura( p[uú]blica| de)", r"ante m[ií].{0,20}notario"],
    "FACT": [r"factura", r"n[ií]?mero de factura", r"base imponible"],
    "FICH": [r"\bficha\b", r"conocimiento del cliente", r"KYC", r"origen de (los )?fondos"],
    "FOTO": [r"reportaje fotogr[aá]fico", r"fotograf[ií]a"],
    "TASA": [r"informe de tasaci[oó]n", r"valor de tasaci[oó]n", r"sociedad de tasaci[oó]n"],
    "CORR": [r"\bde:\s", r"\bpara:\s", r"\basunto:\s"],
    "NOVA": [r"novaci[oó]n", r"cesi[oó]n de cr[eé]dito", r"refinanciaci[oó]n"],
}

QUOTED = re.compile('["“‘’\']([^"”’\']{4,40})["”’\']')


def derive_markers(catalog_path: Path) -> dict[str, list[re.Pattern]]:
    cat = json.loads(catalog_path.read_text(encoding="utf-8-sig"))
    markers: dict[str, list[re.Pattern]] = {}
    for entry in cat:
        code = entry.get("Codigo")
        if not code:
            continue
        desc = entry.get("Descripcion") or ""
        raw: list[str] = []
        # frases entrecomilladas del "CÓMO RECONOCERLA" / autotítulos citados
        for m in QUOTED.findall(desc):
            token = m.strip()
            # descartar codigos y referencias cruzadas triviales
            if token and not re.fullmatch(r"[A-Z]{3,4}(-\d+)?", token) and len(token) >= 4:
                raw.append(re.escape(token))
        raw.extend(CURATED_MARKERS.get(code, []))
        # dedup preservando orden
        seen: set[str] = set()
        pats: list[re.Pattern] = []
        for r in raw:
            if r.lower() in seen:
                continue
            seen.add(r.lower())
            try:
                pats.append(re.compile(r, re.IGNORECASE))
            except re.error:
                continue
        markers[code] = pats
    return markers


def first_pages_text(pdf_path: Path, pages: int) -> str:
    reader = PdfReader(str(pdf_path))
    n = min(pages, len(reader.pages))
    return " ".join(reader.pages[i].extract_text() or "" for i in range(n))


def hits(markers: list[re.Pattern], text: str) -> list[str]:
    found: list[str] = []
    for pat in markers:
        m = pat.search(text)
        if m:
            found.append(re.sub(r"\s+", " ", m.group(0))[:30])
    return found


def verdict(exp: str, pred: str, exp_hits: list[str], pred_hits: list[str]) -> str:
    if exp in PROVENANCE or pred in PROVENANCE:
        return "PROVENANCE"
    if pred_hits and not exp_hits:
        return "MISLABEL_LIKELY"
    if exp_hits and not pred_hits:
        return "REAL_ERROR_LIKELY"
    if exp_hits and pred_hits:
        return "AMBIGUOUS_BOTH"
    return "UNDECIDABLE"


RANK = {
    "MISLABEL_LIKELY": 0,
    "REAL_ERROR_LIKELY": 1,
    "AMBIGUOUS_BOTH": 2,
    "PROVENANCE": 3,
    "UNDECIDABLE": 4,
}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", required=True, help="Directorio del run (contiene results.csv)")
    ap.add_argument("--catalog", default=None, help="catalogotdn1.json (default: junto a este script)")
    ap.add_argument("--corpus-root", default=r"H:\Documentia\ParaNacho\Class")
    ap.add_argument("--pages", type=int, default=5)
    args = ap.parse_args()

    run_dir = Path(args.run)
    results = list(csv.DictReader((run_dir / "results.csv").open(encoding="utf-8-sig"), delimiter=";"))
    catalog_path = Path(args.catalog) if args.catalog else Path(__file__).with_name("catalogotdn1.json")
    if not catalog_path.exists():
        raise SystemExit(f"Falta el catalogo TDN1: {catalog_path}. Descargalo de GET management/catalogotdn1.")
    markers = derive_markers(catalog_path)

    disagreements = [r for r in results if r["predicted_tdn1"] and r["predicted_tdn1"] != r["expected_tdn1"]]
    rows_out = []
    for r in disagreements:
        exp, pred = r["expected_tdn1"], r["predicted_tdn1"].split("-")[0]
        pdf = Path(args.corpus_root) / r["rel_path"].replace("/", os.sep)
        try:
            text = first_pages_text(pdf, args.pages)
        except Exception as exc:  # noqa: BLE001 - registrar y seguir
            rows_out.append({**base_row(r, exp, pred), "veredicto": "PDF_ERROR", "evidencia_exp": "", "evidencia_pred": str(exc)[:60]})
            continue
        eh = hits(markers.get(exp, []), text)
        ph = hits(markers.get(pred, []), text)
        rows_out.append({
            **base_row(r, exp, pred),
            "veredicto": verdict(exp, pred, eh, ph),
            "evidencia_exp": " | ".join(eh),
            "evidencia_pred": " | ".join(ph),
        })

    rows_out.sort(key=lambda x: (RANK.get(x["veredicto"], 9), x["expected_tdn1"], x["predicted_tdn1"]))
    out = run_dir / "audit_groundtruth.csv"
    with out.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(rows_out[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(rows_out)

    summ = Counter(x["veredicto"] for x in rows_out)
    total = len(rows_out)
    print(f"Discrepancias TDN1 auditadas: {total}")
    for k in ["MISLABEL_LIKELY", "REAL_ERROR_LIKELY", "AMBIGUOUS_BOTH", "PROVENANCE", "UNDECIDABLE", "PDF_ERROR"]:
        if summ.get(k):
            print(f"  {k:18s} {summ[k]:3d}  ({100*summ[k]/total:.0f}%)")
    print(f"\nEscrito: {out}")
    print("Revisar primero los MISLABEL_LIKELY (re-etiquetar) y los REAL_ERROR_LIKELY (targets de prompt).")


def base_row(r: dict, exp: str, pred: str) -> dict:
    return {
        "rel_path": r["rel_path"],
        "expected_tdn1": exp,
        "expected_tdn2": r["expected_tdn2"],
        "predicted_tdn1": r["predicted_tdn1"],
        "predicted_tdn2": r["predicted_tdn2"],
        "confianza": r.get("confianza", ""),
    }


if __name__ == "__main__":
    main()
