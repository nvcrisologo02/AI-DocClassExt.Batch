"""Informe consolidado por fichero de un run de evaluacion (AB#99973).

Cruza results.csv (clasificacion), audit_groundtruth.csv (veredictos por contenido)
y audit_notext_db.csv (escaneados recuperados de BD) para producir, por documento:
etiqueta esperada, clasificacion, resultado, propuesta (validada o corregida),
motivos y acciones. Emite informe_por_fichero.csv + un resumen por consola.
"""
from __future__ import annotations

import argparse
import csv
from collections import Counter
from pathlib import Path


def load(path: Path) -> list[dict]:
    if not path.exists():
        return []
    return list(csv.DictReader(path.open(encoding="utf-8-sig"), delimiter=";"))


# Mapea el veredicto del auditor (para discrepancias de TDN1) a resultado/propuesta/accion.
def decide(row: dict, verdict: str | None, ev_exp: str, ev_pred: str) -> dict:
    exp1, exp2 = row["expected_tdn1"], row["expected_tdn2"]
    pred1, pred2 = row["predicted_tdn1"], row["predicted_tdn2"]

    if (row.get("estado") or "OK").upper() not in ("OK", ""):
        return _r("ERROR", "", "", f"Ejecucion {row.get('estado')}: {row.get('error','')}"[:120], "Reprocesar documento")
    if not pred1:
        return _r("SIN CLASIFICAR", "", "", "El clasificador no devolvio tipologia", "Revisar (posible baja confianza / Desconocido)")
    if pred2 == exp2 and pred1 == exp1:
        return _r("OK", exp1, exp2, "Clasificacion coincide con la etiqueta", "Ninguna")
    if pred1 == exp1:  # familia correcta, subtipo distinto
        return _r("REVISAR-TDN2", exp1, "", f"TDN1 OK; TDN2 dif ({pred2} vs {exp2})", "Pendiente auditoria de subtipos TDN2")

    # discrepancia de TDN1: usar veredicto del auditor
    if verdict == "MISLABEL_LIKELY":
        return _r("REVISAR-ETIQUETA", pred1, pred2, f"Contenido respalda al clasificador ({ev_pred})", f"Re-etiquetar a {pred1}")
    if verdict == "REAL_ERROR_LIKELY":
        return _r("REVISAR-CLASIFICADOR", exp1, exp2, f"Contenido respalda la etiqueta ({ev_exp})", f"Target de prompt: {exp1} vs {pred1}")
    if verdict == "PROVENANCE":
        return _r("PROCEDENCIA", "", "", "Familia definida por procedencia/workflow, no decidible por texto", "Escalar a negocio (metadato) - AB#99976")
    if verdict == "AMBIGUOUS_BOTH":
        return _r("REVISAR-HUMANO", "", "", f"Marcadores de ambas familias ({ev_exp} / {ev_pred})", "Revision humana")
    if verdict == "NO_TEXT":
        return _r("REVISAR-ESCANEADO", "", "", "Escaneado sin texto y no recuperado de BD", "Revision humana / OCR")
    # UNDECIDABLE o sin veredicto
    return _r("REVISAR-HUMANO", "", "", "Sin marcadores decisivos en el contenido", "Revision humana")


def _r(resultado, prop1, prop2, motivo, accion) -> dict:
    return {"resultado": resultado, "prop1": prop1, "prop2": prop2, "motivo": motivo, "accion": accion}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", required=True)
    args = ap.parse_args()
    run = Path(args.run)

    results = load(run / "results.csv")
    audit = {r["rel_path"]: r for r in load(run / "audit_groundtruth.csv")}
    notext_db = {r["rel_path"]: r for r in load(run / "audit_notext_db.csv")}

    out_rows = []
    for r in results:
        rel = r["rel_path"]
        a = audit.get(rel)
        verdict = a["veredicto"] if a else None
        ev_exp = a["evidencia_exp"] if a else ""
        ev_pred = a["evidencia_pred"] if a else ""
        # si el auditor base lo marco NO_TEXT y el companion de BD lo recupero, usar ese veredicto
        if verdict == "NO_TEXT" and rel in notext_db:
            d = notext_db[rel]
            verdict, ev_exp, ev_pred = d["veredicto"], d["evidencia_exp"], d["evidencia_pred"]
        dec = decide(r, verdict, ev_exp, ev_pred)
        fn = r["file_name"]
        out_rows.append({
            "filename": fn,
            "nombre": fn[9:],  # quita el prefijo de etiqueta 'XXXX-NN--' (9 chars)
            "tdn1_esperado": r["expected_tdn1"],
            "tdn2_esperado": r["expected_tdn2"],
            "tdn1_clasificado": r["predicted_tdn1"],
            "tdn2_clasificado": r["predicted_tdn2"],
            "resultado": dec["resultado"],
            "tdn1_propuesto": dec["prop1"],
            "tdn2_propuesto": dec["prop2"],
            "motivos": dec["motivo"],
            "acciones": dec["accion"],
        })

    out_rows.sort(key=lambda x: (x["resultado"], x["tdn1_esperado"], x["filename"]))
    out_csv = run / "informe_por_fichero.csv"
    with out_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(out_rows[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(out_rows)

    # Resumen
    total = len(out_rows)
    byres = Counter(x["resultado"] for x in out_rows)
    print(f"Informe por fichero: {total} documentos -> {out_csv}\n")
    print("Resumen por resultado:")
    for k, n in byres.most_common():
        print(f"  {k:22s} {n:4d}  ({100*n/total:.0f}%)")
    print("\nAcciones generales (documentos afectados):")
    byacc = Counter(x["acciones"].split(":")[0].split(" - ")[0] for x in out_rows)
    for k, n in byacc.most_common():
        print(f"  {n:4d}  {k}")


if __name__ == "__main__":
    main()
