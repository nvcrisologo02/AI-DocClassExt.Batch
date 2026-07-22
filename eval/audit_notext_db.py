"""Companion del auditor de ground-truth (AB#99973 / AB#99975): audita los
documentos NO_TEXT (escaneados, sin texto extraible por pypdf) usando el markdown
de Document Intelligence persistido en la BD de DEV.

Obtiene el token Entra por subprocess (NUNCA lo imprime). Lee
Documentos.NormalizacionMarkdownCompressed = base64(gzip(markdown)), lo descomprime
y aplica los mismos marcadores del auditor base. Solo emite veredictos/evidencia,
no el contenido del documento (que puede contener datos personales).

Requiere: pyodbc + ODBC Driver 18 + sesion `az login` activa con acceso de lectura
a la BD DEV. Es de solo lectura.

Uso:
  python eval/audit_notext_db.py --run eval/runs/<dir>
"""
from __future__ import annotations

import argparse
import base64
import csv
import gzip
import struct
import subprocess
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import audit_groundtruth as A  # noqa: E402
import pyodbc  # noqa: E402

SERVER = "srbsqldevdocai.database.windows.net"
DATABASE = "DocumentIA"


def token_struct() -> bytes:
    tok = subprocess.run(
        ["az", "account", "get-access-token", "--resource", "https://database.windows.net/",
         "--query", "accessToken", "-o", "tsv"],
        capture_output=True, text=True, check=True, shell=True,
    ).stdout.strip().encode("utf-16-le")
    return struct.pack(f"<I{len(tok)}s", len(tok), tok)


def decompress(b64: str) -> str:
    raw = base64.b64decode(b64)  # tolera saltos de linea del base64 (.NET InsertLineBreaks)
    try:
        return gzip.decompress(raw).decode("utf-8", "replace")
    except OSError:
        return raw.decode("utf-8", "replace")


def main() -> None:
    ap = argparse.ArgumentParser(description="Audita NO_TEXT (escaneados) via markdown DI de BD DEV.")
    ap.add_argument("--run", required=True, help="Directorio del run con audit_groundtruth.csv")
    ap.add_argument("--catalog", default=str(Path(__file__).with_name("catalogotdn1.json")))
    ap.add_argument("--server", default=SERVER)
    ap.add_argument("--database", default=DATABASE)
    args = ap.parse_args()

    run = Path(args.run)
    audit = list(csv.DictReader((run / "audit_groundtruth.csv").open(encoding="utf-8-sig"), delimiter=";"))
    notext = [r for r in audit if r["veredicto"] == "NO_TEXT"]
    if not notext:
        print("No hay filas NO_TEXT en el audit; nada que recuperar.")
        return
    names = [r["rel_path"].split("/")[-1] for r in notext]
    by_name = {r["rel_path"].split("/")[-1]: r for r in notext}

    markers = A.derive_markers(Path(args.catalog))
    cn = pyodbc.connect(
        f"Driver={{ODBC Driver 18 for SQL Server}};Server={args.server};"
        f"Database={args.database};Encrypt=yes;TrustServerCertificate=no",
        attrs_before={1256: token_struct()},
    )
    placeholders = ",".join("?" * len(names))
    rows = cn.cursor().execute(
        f"SELECT NombreArchivo, NormalizacionMarkdownCompressed FROM Documentos "
        f"WHERE NombreArchivo IN ({placeholders}) AND NormalizacionMarkdownCompressed IS NOT NULL",
        *names,
    ).fetchall()

    seen: set[str] = set()
    out = []
    summ: Counter = Counter()
    for name, comp in rows:
        if name in seen:
            continue
        seen.add(name)
        md = decompress(comp)
        r = by_name[name]
        exp, pred = r["expected_tdn1"], r["predicted_tdn1"].split("-")[0]
        eh = A.hits(markers.get(exp, []), md)
        ph = A.hits(markers.get(pred, []), md)
        v = A.verdict(exp, pred, eh, ph)
        summ[v] += 1
        out.append((r["rel_path"], exp, pred, v, len(md), " | ".join(eh), " | ".join(ph)))

    # Persistir el resultado recuperado junto al run, para completar la revision.
    out_csv = run / "audit_notext_db.csv"
    with out_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["rel_path", "expected_tdn1", "predicted_tdn1", "veredicto", "md_len", "evidencia_exp", "evidencia_pred"])
        for rel, exp, pred, v, ln, eh, ph in sorted(out, key=lambda x: A.RANK.get(x[3], 9)):
            w.writerow([rel, exp, pred, v, ln, eh, ph])

    no_db = len(names) - len(seen)
    print(f"NO_TEXT recuperados de BD: {len(seen)} de {len(names)} ({no_db} no estan en Documentos)")
    for k in ["MISLABEL_LIKELY", "REAL_ERROR_LIKELY", "AMBIGUOUS_BOTH", "PROVENANCE", "UNDECIDABLE"]:
        if summ.get(k):
            print(f"  {k:18s} {summ[k]}")
    print(f"\nEscrito: {out_csv}")


if __name__ == "__main__":
    main()
