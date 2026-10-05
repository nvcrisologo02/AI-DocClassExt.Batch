"""Markdown de cada documento desde Documentos de DEV y PRO, cruzando por SHA256.

Solo SELECT. Misma lectura que audit_notext_db.py (AB#100170): se prefiere la columna
binaria NormalizacionMarkdownGzip y se cae a la Base64 histórica.

Cruce por SHA256: Documentos.SHA256 (nvarchar(64), verificado el 2026-09-25 en DEV:
6522/6522 filas con valor, hex en minúsculas, igual que CACHE/inventario.csv). Se
normaliza igualmente con normalizar_sha256() antes de usarlo como clave, por si PRO
difiere en mayúsculas o espacios.

Uso: python texto.py"""
from __future__ import annotations

import base64
import csv
import gzip
import struct
import zlib
from collections import Counter

import pyodbc

from comun import CACHE, RES_SQL, token

SERVIDORES = [("bd_dev", "srbsqldevdocai.database.windows.net"), ("bd_pro", "srbsqlprodocai.database.windows.net")]
LOTE = 500


def normalizar_sha256(valor) -> str:
    """Homogeneiza el SHA256 leído de BD (o de cualquier otra fuente) al formato hex
    en minúsculas y sin espacios que usa CACHE/inventario.csv."""
    if isinstance(valor, bytes):
        valor = valor.decode("ascii", "ignore")
    return str(valor).strip().lower()


def descomprimir(binario, base64_hist) -> str:
    try:
        if binario:
            return gzip.decompress(bytes(binario)).decode("utf-8", "replace")
        if base64_hist:
            return gzip.decompress(base64.b64decode(base64_hist)).decode("utf-8", "replace")
    except (zlib.error, EOFError):
        # Cabecera gzip válida pero cuerpo corrupto o truncado: no es texto plano
        # recuperable, se degrada a sin_texto en lugar de tumbar la ejecución.
        return ""
    except OSError:
        return (bytes(binario) if binario else base64.b64decode(base64_hist)).decode("utf-8", "replace")
    return ""


def ruta_md(sha: str):
    return CACHE / "md" / f"{sha}.md.gz"


def leer_md(sha: str):
    p = ruta_md(sha)
    return gzip.decompress(p.read_bytes()).decode("utf-8") if p.exists() else None


def guardar_md(sha: str, md: str) -> None:
    p = ruta_md(sha)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_bytes(gzip.compress(md.encode("utf-8")))


def conectar(servidor: str):
    tok = token(RES_SQL).encode("utf-16-le")
    return pyodbc.connect(
        f"Driver={{ODBC Driver 18 for SQL Server}};Server={servidor};Database=DocumentIA;"
        "Encrypt=yes;TrustServerCertificate=no;ApplicationIntent=ReadOnly",
        attrs_before={1256: struct.pack(f"<I{len(tok)}s", len(tok), tok)},
    )


def main() -> None:
    inv = list(csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";"))
    origen = {}
    prev = CACHE / "texto_origen.csv"
    if prev.exists():
        origen = {r["sha256"]: r["origen_texto"] for r in csv.DictReader(prev.open(encoding="utf-8-sig"), delimiter=";")}
    for etiqueta, servidor in SERVIDORES:
        faltan = [r["sha256"] for r in inv if origen.get(r["sha256"], "sin_texto") == "sin_texto"]
        if not faltan:
            break
        cur = conectar(servidor).cursor()
        for i in range(0, len(faltan), LOTE):
            lote = faltan[i:i + LOTE]
            filas = cur.execute(
                "SELECT SHA256, NormalizacionMarkdownGzip, NormalizacionMarkdownCompressed FROM Documentos "
                f"WHERE SHA256 IN ({','.join('?' * len(lote))}) "
                "AND (NormalizacionMarkdownGzip IS NOT NULL OR NormalizacionMarkdownCompressed IS NOT NULL)",
                *lote,
            ).fetchall()
            for sha_bd, binario, b64 in filas:
                sha = normalizar_sha256(sha_bd)
                if sha in origen and origen[sha] != "sin_texto":
                    continue
                md = descomprimir(binario, b64)
                if md.strip():
                    guardar_md(sha, md)
                    origen[sha] = etiqueta
        print(f"{etiqueta}: acumulado con texto {sum(v != 'sin_texto' for v in origen.values())} de {len(inv)}")

    with prev.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["sha256", "origen_texto", "caracteres"])
        for r in inv:
            o = origen.get(r["sha256"], "sin_texto")
            md = leer_md(r["sha256"]) if o != "sin_texto" else ""
            w.writerow([r["sha256"], o, len(md or "")])
    pend = [r for r in inv if origen.get(r["sha256"], "sin_texto") == "sin_texto"]
    with (CACHE / "pendientes_di.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.writer(f, delimiter=";")
        w.writerow(["sha256", "rel_path", "paginas"])
        for r in pend:
            w.writerow([r["sha256"], r["rel_path"], r["paginas"]])
    print("origen:", dict(Counter(origen.get(r["sha256"], "sin_texto") for r in inv)))
    print("pendientes DI:", len(pend), "| por partición:", dict(Counter(r["particion"] for r in pend)))


if __name__ == "__main__":
    main()
