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
    if pred1 == exp1:  # familia correcta, subtipo distinto -> refinado luego con audit_tdn2
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


# Refina un REVISAR-TDN2 con el veredicto del auditor de subtipos (audit_tdn2.csv).
# El split MISLABEL/REAL_ERROR de TDN2 es heuristico -> las acciones llevan "(confirmar)".
def refine_tdn2(dec: dict, row: dict, a2: dict) -> dict:
    exp1, exp2, pred2 = row["expected_tdn1"], row["expected_tdn2"], row["predicted_tdn2"]
    v = a2["veredicto_tdn2"]
    if v in ("CATALOG_DUP", "CATCHALL"):
        motivo = "Subtipos duplicados en catalogo" if v == "CATALOG_DUP" else f"Confusion con subtipo cajon de sastre ({a2['motivo']})"
        return _r("TDN2-CATALOGO", exp1, "", motivo, "Calidad de catalogo TDN2 (AB de catalogo) - no es fallo de clasificador")
    if v == "MISLABEL_LIKELY":
        return _r("TDN2-ETIQUETA", exp1, pred2, f"Subtipo: contenido encaja con {pred2} ({a2['motivo']})", f"Re-etiquetar subtipo a {pred2} (confirmar)")
    if v == "REAL_ERROR_LIKELY":
        return _r("TDN2-CLASIFICADOR", exp1, exp2, f"Subtipo: contenido encaja con {exp2} ({a2['motivo']})", f"Target de prompt subtipo {exp2} vs {pred2} (confirmar)")
    if v == "NO_TEXT":
        return _r("TDN2-ESCANEADO", exp1, "", "Subtipo no auditable: escaneado sin texto", "Recuperar de BD / revision humana")
    return _r("TDN2-HUMANO", exp1, "", f"Subtipo ambiguo/sin señal ({a2['motivo']})", "Revision humana de subtipo")


# Reconcilia el veredicto usando mi clasificacion independiente como decisor
# (para el subconjunto contestado, donde es mas fiable que el auditor por keywords).
def reconcile_mi(dec: dict, row: dict, mi: dict) -> dict:
    exp1, pred1, m1 = row["expected_tdn1"], row["predicted_tdn1"], mi["mi_tdn1"]
    razon = mi.get("razon", "")
    coin = mi["coincide"]
    if coin == "clasificador":  # mi lectura = clasificador -> la etiqueta esta mal
        return _r("RE-ETIQUETAR", m1, "", f"Mi lectura: {razon}", f"Re-etiquetar de {exp1} a {m1}")
    if coin == "etiqueta":  # mi lectura = etiqueta
        if not pred1:
            return _r("CLASIF-SIN-RESPUESTA", m1, "", f"Doc clasificable ({m1}) pero el clasificador no respondio. {razon}", "Revisar robustez del clasificador (no es problema de etiqueta)")
        return _r("CLASIF-FALLO", m1, "", f"Mi lectura confirma la etiqueta {m1}: {razon}", f"Fallo del clasificador ({pred1}); target de prompt {m1} vs {pred1}")
    # ninguno: propongo una tercera tipologia distinta de etiqueta y clasificador
    return _r("TERCERA-OPCION", m1, "", f"Ni etiqueta ({exp1}) ni clasificador ({pred1 or '-'}): mi lectura es {m1}. {razon}", f"Revisar: proponer {m1}")


RESULTADO_DESC = {
    "OK": "TDN1 y TDN2 correctos",
    "REVISAR-TDN2": "TDN1 correcto, subtipo pendiente de auditar",
    "PROCEDENCIA": "Familia definida por procedencia/workflow, no decidible por texto",
    "REVISAR-HUMANO": "Ambiguo o sin marcador decisivo",
    "REVISAR-ETIQUETA": "Ground-truth probablemente erroneo -> re-etiquetar",
    "SIN CLASIFICAR": "El clasificador no devolvio tipologia",
    "REVISAR-CLASIFICADOR": "Fallo real del clasificador (TDN1) -> target de prompt",
    "REVISAR-ESCANEADO": "Escaneado sin texto, no recuperado de BD",
    "TDN2-CATALOGO": "Subtipo: defecto de catalogo (duplicado o cajon de sastre), no del clasificador",
    "TDN2-ETIQUETA": "Subtipo: etiqueta probablemente erronea -> re-etiquetar (confirmar)",
    "TDN2-CLASIFICADOR": "Subtipo: fallo real del clasificador -> target de prompt (confirmar)",
    "TDN2-ESCANEADO": "Subtipo no auditable: escaneado sin texto",
    "TDN2-HUMANO": "Subtipo ambiguo o sin señal -> revision humana",
    "RE-ETIQUETAR": "Mi lectura coincide con el clasificador: la etiqueta del golden esta mal",
    "CLASIF-FALLO": "Mi lectura coincide con la etiqueta: el clasificador fallo -> target de prompt",
    "CLASIF-SIN-RESPUESTA": "Documento clasificable pero el clasificador no devolvio nada -> robustez",
    "TERCERA-OPCION": "Mi lectura difiere de etiqueta Y clasificador: propongo una tercera",
    "ERROR": "Error de ejecucion",
}


def _md_escape(s: str) -> str:
    return (s or "").replace("|", "\\|").replace("\n", " ")


def write_markdown(run: Path, rows: list[dict], byres: Counter, total: int) -> None:
    lines: list[str] = []
    lines.append(f"# Informe de clasificacion por fichero — {run.name}")
    lines.append("")
    lines.append(f"Total documentos: **{total}**. Generado desde `results.csv` + auditor de "
                 "ground-truth (`audit_groundtruth.csv`) + recuperacion de escaneados de BD "
                 "(`audit_notext_db.csv`).")
    lines.append("")
    lines.append("## Cuadro resumen (por resultado)")
    lines.append("")
    lines.append("| Resultado | Docs | % | Descripcion |")
    lines.append("|---|---:|---:|---|")
    for k, n in byres.most_common():
        lines.append(f"| {k} | {n} | {100*n/total:.0f}% | {RESULTADO_DESC.get(k, '')} |")
    lines.append("")
    lines.append("## Acciones generales")
    lines.append("")
    byacc = Counter(x["acciones"].split(":")[0].split(" - ")[0] for x in rows)
    lines.append("| Accion | Docs |")
    lines.append("|---|---:|")
    for k, n in byacc.most_common():
        lines.append(f"| {k} | {n} |")
    lines.append("")
    lines.append("## Detalle por fichero")
    lines.append("")
    lines.append("Ordenado por resultado. `propuesto` = tipologia validada (si OK) o corregida por el auditor.")
    lines.append("")
    hdr = ["nombre", "TDN1 esp", "TDN2 esp", "TDN1 clas", "TDN2 clas", "resultado",
           "TDN1 mio", "TDN2 mio", "coincide", "conf", "acciones"]
    lines.append("| " + " | ".join(hdr) + " |")
    lines.append("|" + "|".join(["---"] * len(hdr)) + "|")
    for r in rows:
        lines.append("| " + " | ".join(_md_escape(str(x)) for x in [
            r["nombre"], r["tdn1_esperado"], r["tdn2_esperado"], r["tdn1_clasificado"],
            r["tdn2_clasificado"], r["resultado"], r.get("tdn1_mi_criterio") or "-",
            r.get("tdn2_mi_criterio") or "-", r.get("mi_coincide_con") or "-",
            r.get("mi_confianza") or "-", r["acciones"],
        ]) + " |")
    (run / "informe_por_fichero.md").write_text("\n".join(lines), encoding="utf-8")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--run", required=True)
    args = ap.parse_args()
    run = Path(args.run)

    results = load(run / "results.csv")
    audit = {r["rel_path"]: r for r in load(run / "audit_groundtruth.csv")}
    notext_db = {r["rel_path"]: r for r in load(run / "audit_notext_db.csv")}
    tdn2 = {r["filename"]: r for r in load(run / "audit_tdn2.csv")}
    mio = {r["filename"]: r for r in load(run / "mi_criterio.csv")}  # clasificacion independiente (subconjunto contestado)

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
        if dec["resultado"] == "REVISAR-TDN2" and r["file_name"] in tdn2:
            dec = refine_tdn2(dec, r, tdn2[r["file_name"]])
        fn = r["file_name"]
        mi = mio.get(fn)  # mi criterio independiente (solo subconjunto contestado)
        # Para el subconjunto contestado, mi lectura independiente MANDA sobre el auditor
        # automatico (por keywords, menos fiable): reconcilia resultado/motivo/accion.
        if mi and mi["coincide"] != "ambos":
            dec = reconcile_mi(dec, r, mi)
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
            "tdn1_mi_criterio": mi["mi_tdn1"] if mi else "",
            "tdn2_mi_criterio": mi["mi_tdn2"] if mi else "",
            "mi_coincide_con": mi["coincide"] if mi else "",
            "mi_confianza": mi["confianza"] if mi else "",
            "mi_razon": mi["razon"] if mi else "",
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
    write_markdown(run, out_rows, byres, total)
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
