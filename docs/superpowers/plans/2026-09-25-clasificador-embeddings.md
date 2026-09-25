# Prueba de clasificador por embeddings: plan de implementación

> **Para agentes:** SUB-SKILL OBLIGATORIA: usar superpowers:subagent-driven-development (recomendada) o superpowers:executing-plans para ejecutar este plan tarea a tarea. Los pasos usan casillas (`- [ ]`) para el seguimiento.

**Objetivo:** medir fuera de línea si un clasificador de embeddings con confianza calibrada (A: regresión logística jerárquica, B: kNN), más gpt-4.1-mini con logprobs (C) y un híbrido con GPT (H), cumple los objetivos O1-O5 sobre el golden de 467.

**Arquitectura:** scripts Python independientes en `eval/clasificador-embeddings/` que se encadenan a través de una caché local indexada por SHA256: inventario → texto (BD DEV, BD PRO, DI) → embeddings → modelos → evaluación e informe. Solo hay lecturas en BD y llamadas de inferencia a recursos de DEV que ya existen.

**Stack:** Python 3.13, numpy, scikit-learn, pyodbc (ODBC Driver 18), requests + truststore, pypdf, pytest. Tokens de Entra con `az account get-access-token`.

**Especificación:** `docs/superpowers/specs/2026-09-25-clasificador-embeddings-design.md`

## Restricciones globales

- Work item: todos los commits llevan `AB#100719`. Rama `feature/100719-clasificador-embeddings`. Sin push sin petición explícita.
- Sin coautores ni menciones de modelos o proveedores de IA en commits, comentarios ni documentos.
- Español en comentarios, mensajes y documentos. Identificadores en español, como el resto de `eval/`.
- **Ninguna escritura** en DEV ni en PRO: en SQL solo `SELECT`; en Azure solo llamadas de inferencia. No se crean ni modifican deployments, configuración ni filas.
- DI Layout de DEV **no se lanza** sin antes enseñar el recuento de documentos y páginas y el coste estimado, y recibir un sí explícito del usuario.
- Caché: `C:/temp/MVP/spike-embeddings-cache/` (fuera de los repos, contiene texto sensible, nunca se versiona). Resultados por fichero: `eval/runs/<fecha>-clasificador-embeddings/` (ya en `.gitignore`).
- El nombre del fichero **nunca** entra en ningún modelo: solo sirve para sacar la etiqueta.
- Recursos (verificados el 2026-09-25):
  - Embeddings: `https://srbaisrv02devdocai.cognitiveservices.azure.com`, deployment `text-embedding-3-large-010650`.
  - Chat: `https://srbaisrv01devdocai.cognitiveservices.azure.com`, deployment `gpt-4o-mini`, que es gpt-4.1-mini.
  - DI: `https://srbdidevdocai.cognitiveservices.azure.com`.
  - BD DEV: `srbsqldevdocai.database.windows.net` / `DocumentIA`. BD PRO: `srbsqlprodocai.database.windows.net` / `DocumentIA`.
  - No usar `*.openai.azure.com` (bloqueado por el filtro web corporativo).
- Referencia de comparación: `eval/runs/BASELINE-GPT4OMINI-DEV/results.csv` (TDN1 55,89 %, TDN2 33,40 %). La vista "sin PROCEDENCIA" usa `eval/runs/BASELINE-FINAL-CONSOLIDADO/informe_por_fichero.csv` (`resultado == "PROCEDENCIA"`, 72 filas, clave `filename`).
- Umbrales (§5 de la especificación): O1 ≥ 53,9 % TDN1 y ≥ 31,4 % TDN2, fracaso < 50,9 % TDN1; O2 ECE ≤ 0,05, fracaso > 0,10; O3 cobertura ≥ 40 % con acierto TDN1 ≥ 90 % y umbral fijado en calibración, fracaso < 20 %; O4 ≥ +3 pp con McNemar p < 0,05; O5 < 10 % del coste GPT y p95 < 1 s.

## Estructura de ficheros

```
eval/clasificador-embeddings/
  requirements.txt      dependencias del venv
  comun.py              rutas, tokens, SHA256, HTTP con truststore, lectura de la caché
  inventario.py         recorre H:, parsea etiquetas, dedup por hash, particiones → inventario.csv
  texto.py              markdown desde BD DEV/PRO por SHA256, recuento de lo que falta
  di_layout.py          DI Layout de DEV para lo que falta (con autorización previa)
  embeddings.py         vectores por hash → embeddings.npz
  modelos.py            A (LR jerárquica), B (kNN), calibración isotónica
  metricas.py           acierto, ECE, cobertura con acierto mínimo, McNemar exacto
  evaluar.py            entrena, calibra, evalúa en el golden, híbrido, informe
  logprobs_gpt.py       enfoque C sobre el golden
  tests/                pytest de las funciones puras
  INFORME.md            resultado final (tarea 9)
```

---

### Tarea 1: Entorno e inventario del corpus

**Ficheros:**
- Crear: `eval/clasificador-embeddings/requirements.txt`, `comun.py`, `inventario.py`, `tests/test_inventario.py`
- Modificar: `.gitignore` (añadir `eval/clasificador-embeddings/.venv/`)

**Interfaces:**
- Produce:
  - `comun.CACHE: Path`, `comun.sha256_fichero(p: Path) -> str` (hex en minúsculas).
  - `comun.token(resource: str) -> str`, `comun.sesion_http() -> requests.Session`.
  - `inventario.parsear_etiqueta(carpeta: str, nombre: str) -> tuple[str|None, str|None, str]` devuelve `(tdn1, tdn2, motivo)`, con `motivo` ∈ `{"ok","sin_tdn2","carpeta_invalida","conflicto_carpeta"}`.
  - `inventario.particionar(filas: list[dict], semilla: int = 42, frac_cal: float = 0.2) -> list[dict]`, que añade `particion` ∈ `{"train","cal","golden"}`.
  - Fichero `CACHE/inventario.csv` (`;`, utf-8-sig) con columnas `sha256;rel_path;tdn1;tdn2;particion;paginas`.

- [ ] **Paso 1: venv y dependencias**

`eval/clasificador-embeddings/requirements.txt`:
```
numpy>=2.0
scikit-learn>=1.5
pyodbc>=5.1
requests>=2.32
truststore>=0.9
pypdf>=5.0
pytest>=8.0
```
Ejecutar:
```bash
cd /c/temp/MVP/DocumentIA.Batch/eval/clasificador-embeddings
python -m venv .venv && .venv/Scripts/python -m pip install -q -r requirements.txt && .venv/Scripts/python -c "import sklearn,truststore;print(sklearn.__version__)"
```
Añadir `eval/clasificador-embeddings/.venv/` a `.gitignore`.

- [ ] **Paso 2: test de las funciones puras (falla)**

`tests/test_inventario.py`:
```python
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inventario import parsear_etiqueta, particionar


def test_etiqueta_completa():
    assert parsear_etiqueta("CEDU", "CEDU-01--OP-99-SCXX-00_ATINF00001579_106149101.pdf") == ("CEDU", "CEDU-01", "ok")


def test_sin_tdn2_usa_carpeta():
    assert parsear_etiqueta("BORR", "BORRADOR--OP-99-SCXX-00_X_1.pdf") == ("BORR", None, "sin_tdn2")


def test_carpeta_invalida():
    assert parsear_etiqueta("NO S", "loquesea.pdf") == (None, None, "carpeta_invalida")


def test_prefijo_distinto_de_carpeta_es_conflicto():
    assert parsear_etiqueta("NOTS", "ESIN-34--OP-99_X_1.pdf") == (None, None, "conflicto_carpeta")


def test_particion_respeta_golden_y_estratifica():
    filas = [{"sha256": f"h{i}", "tdn1": "AAAA" if i % 2 else "BBBB", "golden": False} for i in range(100)]
    filas[0]["golden"] = True
    out = particionar(filas, semilla=1, frac_cal=0.2)
    assert out[0]["particion"] == "golden"
    cal = [f for f in out if f["particion"] == "cal"]
    assert 15 <= len(cal) <= 25
    assert {f["tdn1"] for f in cal} == {"AAAA", "BBBB"}
    assert particionar(filas, semilla=1) == out  # determinista
```
Ejecutar: `.venv/Scripts/python -m pytest tests/test_inventario.py -v` → FALLA (`ModuleNotFoundError: inventario`).

- [ ] **Paso 3: `comun.py`**

```python
"""Utilidades comunes de la prueba de clasificador por embeddings (AB#100719).

Solo lectura: tokens de Entra con az CLI, hash de ficheros y sesión HTTP que usa el
almacén de certificados de Windows (el proxy corporativo inspecciona TLS)."""
from __future__ import annotations

import hashlib
import subprocess
from pathlib import Path

import requests
import truststore

truststore.inject_into_ssl()

CACHE = Path("C:/temp/MVP/spike-embeddings-cache")
CORPUS = Path("H:/Documentia/ParaNacho/Class")
EVAL = Path(__file__).resolve().parents[1]
GOLDEN_CSV = EVAL / "golden.csv"
BASELINE_CSV = EVAL / "runs" / "BASELINE-GPT4OMINI-DEV" / "results.csv"
INFORME_FICHERO_CSV = EVAL / "runs" / "BASELINE-FINAL-CONSOLIDADO" / "informe_por_fichero.csv"

EMB_ENDPOINT = "https://srbaisrv02devdocai.cognitiveservices.azure.com"
EMB_DEPLOYMENT = "text-embedding-3-large-010650"
CHAT_ENDPOINT = "https://srbaisrv01devdocai.cognitiveservices.azure.com"
CHAT_DEPLOYMENT = "gpt-4o-mini"  # es gpt-4.1-mini 2025-04-14
DI_ENDPOINT = "https://srbdidevdocai.cognitiveservices.azure.com"
RES_COGNITIVE = "https://cognitiveservices.azure.com"
RES_SQL = "https://database.windows.net/"


def sha256_fichero(p: Path) -> str:
    h = hashlib.sha256()
    with p.open("rb") as f:
        for bloque in iter(lambda: f.read(1 << 20), b""):
            h.update(bloque)
    return h.hexdigest()


def token(resource: str) -> str:
    """Token de Entra de la sesión az; nunca se imprime."""
    return subprocess.run(
        ["az", "account", "get-access-token", "--resource", resource, "--query", "accessToken", "-o", "tsv"],
        capture_output=True, text=True, check=True, shell=True,
    ).stdout.strip()


def sesion_http() -> requests.Session:
    s = requests.Session()
    s.headers["Authorization"] = f"Bearer {token(RES_COGNITIVE)}"
    return s
```

- [ ] **Paso 4: `inventario.py`**

```python
"""Inventario del corpus validado: etiqueta por nombre, dedup por SHA256 y particiones.

Uso: python inventario.py [--corpus H:/Documentia/ParaNacho/Class]
Escribe CACHE/inventario.csv y CACHE/inventario_excluidos.csv."""
from __future__ import annotations

import argparse
import csv
import random
import re
from collections import Counter, defaultdict
from pathlib import Path

from pypdf import PdfReader

from comun import CACHE, CORPUS, GOLDEN_CSV, sha256_fichero

RE_TDN1 = re.compile(r"^[A-Z]{4}$")
RE_TDN2 = re.compile(r"^([A-Z]{4})-(\d{2})--")


def parsear_etiqueta(carpeta: str, nombre: str):
    if not RE_TDN1.match(carpeta):
        return None, None, "carpeta_invalida"
    m = RE_TDN2.match(nombre)
    if not m:
        return carpeta, None, "sin_tdn2"
    if m.group(1) != carpeta:
        return None, None, "conflicto_carpeta"
    return carpeta, f"{m.group(1)}-{m.group(2)}", "ok"


def particionar(filas, semilla: int = 42, frac_cal: float = 0.2):
    """Golden fuera; el resto 80/20 estratificado por TDN1 y determinista."""
    out = [dict(f) for f in filas]
    por_tdn1 = defaultdict(list)
    for i, f in enumerate(out):
        if f["golden"]:
            f["particion"] = "golden"
        else:
            por_tdn1[f["tdn1"]].append(i)
    rnd = random.Random(semilla)
    for tdn1 in sorted(por_tdn1):
        idx = sorted(por_tdn1[tdn1], key=lambda i: out[i]["sha256"])
        rnd.shuffle(idx)
        n_cal = round(len(idx) * frac_cal)
        for k, i in enumerate(idx):
            out[i]["particion"] = "cal" if k < n_cal else "train"
    return out


def paginas(p: Path) -> int:
    try:
        return len(PdfReader(str(p)).pages)
    except Exception:
        return -1


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--corpus", default=str(CORPUS))
    args = ap.parse_args()
    raiz = Path(args.corpus)
    golden_rel = {r["rel_path"].lower() for r in csv.DictReader(GOLDEN_CSV.open(encoding="utf-8-sig"), delimiter=";")}

    por_hash: dict[str, list[dict]] = defaultdict(list)
    excluidos, motivos = [], Counter()
    for pdf in sorted(raiz.glob("*/*")):
        if pdf.suffix.lower() != ".pdf":
            continue
        rel = f"{pdf.parent.name}/{pdf.name}"
        tdn1, tdn2, motivo = parsear_etiqueta(pdf.parent.name, pdf.name)
        motivos[motivo] += 1
        if tdn1 is None:
            excluidos.append({"rel_path": rel, "motivo": motivo})
            continue
        por_hash[sha256_fichero(pdf)].append(
            {"rel_path": rel, "tdn1": tdn1, "tdn2": tdn2 or "", "golden": rel.lower() in golden_rel, "ruta": pdf})

    filas = []
    for h, grupo in por_hash.items():
        etiquetas = {(g["tdn1"], g["tdn2"]) for g in grupo if g["tdn2"]} or {(grupo[0]["tdn1"], "")}
        if len({e[0] for e in etiquetas}) > 1 or len({e[1] for e in etiquetas if e[1]}) > 1:
            excluidos += [{"rel_path": g["rel_path"], "motivo": "conflicto_hash"} for g in grupo]
            motivos["conflicto_hash"] += len(grupo)
            continue
        tdn1, tdn2 = sorted(etiquetas)[-1]
        rep = next((g for g in grupo if g["golden"]), grupo[0])
        filas.append({"sha256": h, "rel_path": rep["rel_path"], "tdn1": tdn1, "tdn2": tdn2,
                      "golden": any(g["golden"] for g in grupo), "paginas": paginas(rep["ruta"])})

    filas = particionar(filas)
    CACHE.mkdir(parents=True, exist_ok=True)
    with (CACHE / "inventario.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["sha256", "rel_path", "tdn1", "tdn2", "particion", "paginas"], delimiter=";", extrasaction="ignore")
        w.writeheader()
        w.writerows(filas)
    with (CACHE / "inventario_excluidos.csv").open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["rel_path", "motivo"], delimiter=";")
        w.writeheader()
        w.writerows(excluidos)
    print("motivos:", dict(motivos))
    print("únicos por hash:", len(filas), "| particiones:", dict(Counter(f["particion"] for f in filas)))
    print("golden encontrados:", sum(f["golden"] for f in filas), "de", len(golden_rel))


if __name__ == "__main__":
    main()
```

- [ ] **Paso 5: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/test_inventario.py -v` → 5 passed.

- [ ] **Paso 6: ejecución real**

Ejecutar: `.venv/Scripts/python inventario.py`. Tarda (hashea unos 8.500 PDF desde H:).
Esperado: `golden encontrados` cercano a 467. Si faltan, listar cuáles y avisar antes de seguir (puede haber duplicados por hash entre golden y resto, o ficheros movidos). Anotar en la salida los recuentos por motivo.

- [ ] **Paso 7: commit**

```bash
git add .gitignore eval/clasificador-embeddings/requirements.txt eval/clasificador-embeddings/comun.py eval/clasificador-embeddings/inventario.py eval/clasificador-embeddings/tests/test_inventario.py
git commit -m "feat(eval): inventario del corpus con dedup por SHA256 y particiones para la prueba de embeddings (AB#100719)"
```

---

### Tarea 2: Texto desde BD DEV y BD PRO (solo lectura)

**Ficheros:**
- Crear: `eval/clasificador-embeddings/texto.py`, `tests/test_texto.py`

**Interfaces:**
- Consume: `CACHE/inventario.csv`, `comun.token`, `comun.RES_SQL`.
- Produce:
  - `texto.descomprimir(binario: bytes|None, base64_hist: str|None) -> str`.
  - `texto.ruta_md(sha: str) -> Path` (`CACHE/md/<sha>.md.gz`).
  - `texto.leer_md(sha: str) -> str|None`.
  - `CACHE/texto_origen.csv` con `sha256;origen_texto;caracteres`, donde `origen_texto` ∈ `{"bd_dev","bd_pro","di_dev","sin_texto"}`.
  - `CACHE/pendientes_di.csv` con `sha256;rel_path;paginas`.

- [ ] **Paso 1: test (falla)**

`tests/test_texto.py`:
```python
import base64
import gzip
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from texto import descomprimir


def test_prefiere_binario():
    assert descomprimir(gzip.compress("hola".encode()), base64.b64encode(gzip.compress(b"viejo")).decode()) == "hola"


def test_cae_a_base64_historico_con_saltos():
    b64 = base64.b64encode(gzip.compress("año".encode())).decode()
    assert descomprimir(None, b64[:4] + "\r\n" + b64[4:]) == "año"


def test_vacio():
    assert descomprimir(None, None) == ""
```
Ejecutar: `.venv/Scripts/python -m pytest tests/test_texto.py -v` → FALLA.

- [ ] **Paso 2: `texto.py`**

```python
"""Markdown de cada documento desde Documentos de DEV y PRO, cruzando por SHA256.

Solo SELECT. Misma lectura que audit_notext_db.py (AB#100170): se prefiere la columna
binaria NormalizacionMarkdownGzip y se cae a la Base64 histórica.
Uso: python texto.py"""
from __future__ import annotations

import base64
import csv
import gzip
import struct
from collections import Counter

import pyodbc

from comun import CACHE, RES_SQL, token

SERVIDORES = [("bd_dev", "srbsqldevdocai.database.windows.net"), ("bd_pro", "srbsqlprodocai.database.windows.net")]
LOTE = 500


def descomprimir(binario, base64_hist) -> str:
    try:
        if binario:
            return gzip.decompress(bytes(binario)).decode("utf-8", "replace")
        if base64_hist:
            return gzip.decompress(base64.b64decode(base64_hist)).decode("utf-8", "replace")
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
            for sha, binario, b64 in filas:
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
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/ -v` → todos passed.

- [ ] **Paso 4: ejecución real**

Ejecutar: `.venv/Scripts/python texto.py`. Anotar el reparto por origen y los pendientes de DI. Si `bd_pro` da error de permisos, parar y contárselo al usuario; no buscar otra vía.

- [ ] **Paso 5: commit**

```bash
git add eval/clasificador-embeddings/texto.py eval/clasificador-embeddings/tests/test_texto.py
git commit -m "feat(eval): markdown desde BD DEV y PRO por SHA256 en solo lectura para la prueba de embeddings (AB#100719)"
```

---

### Tarea 3: DI Layout para los pendientes (con puerta de autorización)

**Ficheros:**
- Crear: `eval/clasificador-embeddings/di_layout.py`, `tests/test_di_layout.py`

**Interfaces:**
- Consume: `CACHE/pendientes_di.csv`, `texto.guardar_md`, `comun.sesion_http`, `comun.DI_ENDPOINT`, `comun.CORPUS`.
- Produce: `di_layout.paginas_facturables(paginas: int, max_paginas: int) -> int`. Añade filas `di_dev` a `CACHE/texto_origen.csv`.

- [ ] **Paso 1: test (falla)**

`tests/test_di_layout.py`:
```python
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from di_layout import paginas_facturables


def test_recorte():
    assert paginas_facturables(12, 5) == 5
    assert paginas_facturables(3, 5) == 3


def test_paginas_desconocidas_cuentan_el_maximo():
    assert paginas_facturables(-1, 5) == 5
```

- [ ] **Paso 2: `di_layout.py`**

```python
"""DI Layout de DEV sobre los PDF sin markdown en BD. Dos modos:
  --estimar   solo recuento de documentos y páginas facturables (no llama a DI)
  --lanzar    llama a DI; solo tras autorización explícita del usuario
Uso: python di_layout.py --estimar | --lanzar [--max-paginas 5] [--solo-golden]"""
from __future__ import annotations

import argparse
import base64
import csv
import time

from comun import CACHE, CORPUS, DI_ENDPOINT, sesion_http
from texto import guardar_md

API = "2024-11-30"


def paginas_facturables(paginas: int, max_paginas: int) -> int:
    return max_paginas if paginas < 0 else min(paginas, max_paginas)


def analizar(s, pdf_bytes: bytes, max_paginas: int) -> str:
    url = (f"{DI_ENDPOINT}/documentintelligence/documentModels/prebuilt-layout:analyze"
           f"?api-version={API}&outputContentFormat=markdown&pages=1-{max_paginas}")
    r = s.post(url, json={"base64Source": base64.b64encode(pdf_bytes).decode()}, timeout=120)
    r.raise_for_status()
    op = r.headers["Operation-Location"]
    for _ in range(120):
        time.sleep(2)
        j = s.get(op, timeout=60).json()
        if j["status"] == "succeeded":
            return j["analyzeResult"]["content"]
        if j["status"] == "failed":
            raise RuntimeError(j.get("error"))
    raise TimeoutError(op)


def main() -> None:
    ap = argparse.ArgumentParser()
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--estimar", action="store_true")
    g.add_argument("--lanzar", action="store_true")
    ap.add_argument("--max-paginas", type=int, default=5)
    ap.add_argument("--solo-golden", action="store_true")
    args = ap.parse_args()

    pend = list(csv.DictReader((CACHE / "pendientes_di.csv").open(encoding="utf-8-sig"), delimiter=";"))
    if args.solo_golden:
        inv = {r["sha256"]: r["particion"] for r in csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";")}
        pend = [p for p in pend if inv.get(p["sha256"]) == "golden"]
    total = sum(paginas_facturables(int(p["paginas"]), args.max_paginas) for p in pend)
    print(f"documentos: {len(pend)} | páginas facturables (recorte {args.max_paginas}): {total}")
    if args.estimar:
        return

    s = sesion_http()
    origen_csv = CACHE / "texto_origen.csv"
    filas = list(csv.DictReader(origen_csv.open(encoding="utf-8-sig"), delimiter=";"))
    idx = {f["sha256"]: f for f in filas}
    ok = err = 0
    for p in pend:
        try:
            md = analizar(s, (CORPUS / p["rel_path"]).read_bytes(), args.max_paginas)
            if md.strip():
                guardar_md(p["sha256"], md)
                idx[p["sha256"]].update(origen_texto="di_dev", caracteres=str(len(md)))
                ok += 1
        except Exception as e:  # se registra y se sigue; el documento queda sin_texto
            err += 1
            print("error", p["rel_path"], type(e).__name__)
        if (ok + err) % 100 == 0:
            s = sesion_http()  # renueva el token en ejecuciones largas
    with origen_csv.open("w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, ["sha256", "origen_texto", "caracteres"], delimiter=";")
        w.writeheader()
        w.writerows(filas)
    print(f"DI ok {ok} | errores {err}")


if __name__ == "__main__":
    main()
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/ -v` → todos passed.

- [ ] **Paso 4: estimación y PUERTA**

Ejecutar: `.venv/Scripts/python di_layout.py --estimar` y `--estimar --solo-golden`.
Consultar el precio vigente de DI Layout (Microsoft Learn o la página de precios de Azure) y presentar al usuario: documentos, páginas facturables y coste estimado de las dos variantes (todo el corpus o solo el golden). **PARAR y esperar el sí.** Si dice "solo golden" o "no", seguir con esa decisión; los documentos sin texto quedan fuera del entrenamiento.

- [ ] **Paso 5: lanzamiento autorizado**

Ejecutar: `.venv/Scripts/python di_layout.py --lanzar [--solo-golden]`. Tiene que conectar con DEV desde la primera llamada; si da error TLS o 401, parar y diagnosticar sin cambiar de endpoint.

- [ ] **Paso 6: commit**

```bash
git add eval/clasificador-embeddings/di_layout.py eval/clasificador-embeddings/tests/test_di_layout.py
git commit -m "feat(eval): DI Layout de DEV para documentos sin markdown, con estimación previa de páginas (AB#100719)"
```

---

### Tarea 4: Embeddings

**Ficheros:**
- Crear: `eval/clasificador-embeddings/embeddings.py`, `tests/test_embeddings.py`

**Interfaces:**
- Consume: `CACHE/inventario.csv`, `CACHE/texto_origen.csv`, `texto.leer_md`, `comun.sesion_http`.
- Produce:
  - `embeddings.recortar(md: str, max_chars: int = 24000) -> str`.
  - `embeddings.cargar() -> tuple[list[str], numpy.ndarray]` devuelve `(shas, X)` con X de float32, filas normalizadas L2 y dimensión 3072.
  - Fichero `CACHE/embeddings.npz` (`shas`, `X`) y `CACHE/embeddings_uso.csv` (tokens y latencias por lote).

- [ ] **Paso 1: test (falla)**

`tests/test_embeddings.py`:
```python
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from embeddings import recortar


def test_recorte_por_caracteres():
    assert recortar("a" * 30000) == "a" * 24000


def test_texto_corto_intacto_y_colapsa_espacios():
    assert recortar("hola   \n\n\n mundo") == "hola mundo"
```

- [ ] **Paso 2: `embeddings.py`**

```python
"""Embeddings text-embedding-3-large (DEV, srbaisrv02devdocai) del markdown de cada documento.

Se toma el principio del documento (~8.000 tokens ≈ 24.000 caracteres). Si el servicio
rechaza la entrada por longitud, se reintenta a la mitad. Reanudable: solo calcula los
hashes que faltan en embeddings.npz.
Uso: python embeddings.py"""
from __future__ import annotations

import csv
import re
import time

import numpy as np

from comun import CACHE, EMB_DEPLOYMENT, EMB_ENDPOINT, sesion_http
from texto import leer_md

API = "2024-10-21"
LOTE = 16
NPZ = CACHE / "embeddings.npz"


def recortar(md: str, max_chars: int = 24000) -> str:
    return re.sub(r"\s+", " ", md).strip()[:max_chars]


def cargar():
    d = np.load(NPZ, allow_pickle=False)
    return list(d["shas"]), d["X"]


def pedir(s, textos):
    url = f"{EMB_ENDPOINT}/openai/deployments/{EMB_DEPLOYMENT}/embeddings?api-version={API}"
    for intento in range(6):
        r = s.post(url, json={"input": textos}, timeout=120)
        if r.status_code == 429:
            time.sleep(int(r.headers.get("retry-after", 10)))
            continue
        if r.status_code == 400 and "maximum context length" in r.text:
            textos = [t[: len(t) // 2] for t in textos]
            continue
        r.raise_for_status()
        j = r.json()
        return [e["embedding"] for e in sorted(j["data"], key=lambda e: e["index"])], j["usage"]["prompt_tokens"]
    raise RuntimeError("embeddings: reintentos agotados")


def main() -> None:
    con_texto = [r["sha256"] for r in csv.DictReader((CACHE / "texto_origen.csv").open(encoding="utf-8-sig"), delimiter=";")
                 if r["origen_texto"] != "sin_texto"]
    shas, X = (cargar() if NPZ.exists() else ([], np.zeros((0, 3072), np.float32)))
    hechos = set(shas)
    faltan = [h for h in con_texto if h not in hechos]
    print("por calcular:", len(faltan))
    s = sesion_http()
    nuevos, vecs = [], []
    uso = (CACHE / "embeddings_uso.csv").open("a", newline="", encoding="utf-8")
    for i in range(0, len(faltan), LOTE):
        lote = faltan[i:i + LOTE]
        t0 = time.perf_counter()
        emb, tokens = pedir(s, [recortar(leer_md(h) or "") for h in lote])
        uso.write(f"{len(lote)};{tokens};{time.perf_counter() - t0:.3f}\n")
        nuevos += lote
        vecs += emb
        if len(nuevos) % 800 == 0 or i + LOTE >= len(faltan):
            V = np.asarray(vecs, np.float32)
            V /= np.linalg.norm(V, axis=1, keepdims=True)
            shas, X = shas + nuevos, np.vstack([X, V])
            np.savez(NPZ, shas=np.array(shas), X=X)
            nuevos, vecs = [], []
            s = sesion_http()
            print("guardados:", len(shas))
    uso.close()


if __name__ == "__main__":
    main()
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/ -v` → todos passed.

- [ ] **Paso 4: prueba de conexión y ejecución**

Primero probar la conexión a mano con una sola entrada:
```bash
.venv/Scripts/python -c "from comun import sesion_http; from embeddings import pedir; e,t=pedir(sesion_http(),['prueba de conexión']); print(len(e[0]), t)"
```
Esperado: `3072 <n>`. Si falla por TLS, 401 o 403, parar y diagnosticar.
Después: `.venv/Scripts/python embeddings.py`.

- [ ] **Paso 5: commit**

```bash
git add eval/clasificador-embeddings/embeddings.py eval/clasificador-embeddings/tests/test_embeddings.py
git commit -m "feat(eval): embeddings del markdown con el deployment de DEV, reanudables por hash (AB#100719)"
```

---

### Tarea 5: Métricas

**Ficheros:**
- Crear: `eval/clasificador-embeddings/metricas.py`, `tests/test_metricas.py`

**Interfaces:**
- Produce:
  - `acierto(y, p) -> float`.
  - `ece(confianza, correcto, n_bins=10) -> float`.
  - `umbral_para_acierto(confianza, correcto, objetivo=0.90) -> float` devuelve el menor umbral t tal que el acierto de `{conf ≥ t}` es ≥ objetivo; `inf` si no existe.
  - `cobertura_y_acierto(confianza, correcto, t) -> tuple[float, float]`.
  - `mcnemar(correcto_a, correcto_b) -> tuple[int, int, float]` devuelve `(b, c, p)` exacto bilateral.
  - Todas reciben secuencias o arrays de numpy.

- [ ] **Paso 1: test (falla)**

`tests/test_metricas.py`:
```python
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from metricas import acierto, cobertura_y_acierto, ece, mcnemar, umbral_para_acierto


def test_acierto():
    assert acierto(["A", "B", "C", "D"], ["A", "B", "X", "D"]) == 0.75


def test_ece_perfectamente_calibrado_es_cero():
    conf = [0.8] * 10
    corr = [1] * 8 + [0] * 2
    assert math.isclose(ece(conf, corr), 0.0, abs_tol=1e-9)


def test_ece_sobreconfiado():
    assert math.isclose(ece([1.0] * 4, [1, 0, 1, 0]), 0.5, abs_tol=1e-9)


def test_umbral_y_cobertura():
    conf = [0.99, 0.95, 0.9, 0.6, 0.5]
    corr = [1, 1, 1, 0, 1]
    t = umbral_para_acierto(conf, corr, 0.90)
    assert t == 0.9
    assert cobertura_y_acierto(conf, corr, t) == (0.6, 1.0)


def test_umbral_inexistente():
    assert umbral_para_acierto([0.9, 0.8], [0, 0], 0.9) == float("inf")


def test_mcnemar_exacto():
    b, c, p = mcnemar([1] * 10 + [0] * 0, [0] * 10)
    assert (b, c) == (10, 0)
    assert math.isclose(p, 2 * 0.5 ** 10)
    assert mcnemar([1, 0], [1, 0])[2] == 1.0
```

- [ ] **Paso 2: `metricas.py`**

```python
"""Métricas de la prueba: acierto, calibración (ECE), cobertura con acierto mínimo y McNemar."""
from __future__ import annotations

from math import comb

import numpy as np


def acierto(y, p) -> float:
    y, p = np.asarray(y), np.asarray(p)
    return float((y == p).mean()) if len(y) else 0.0


def ece(confianza, correcto, n_bins: int = 10) -> float:
    c, k = np.asarray(confianza, float), np.asarray(correcto, float)
    bins = np.minimum((c * n_bins).astype(int), n_bins - 1)
    total = 0.0
    for b in range(n_bins):
        m = bins == b
        if m.any():
            total += m.sum() / len(c) * abs(k[m].mean() - c[m].mean())
    return float(total)


def umbral_para_acierto(confianza, correcto, objetivo: float = 0.90) -> float:
    c, k = np.asarray(confianza, float), np.asarray(correcto, float)
    orden = np.argsort(-c, kind="stable")
    c, k = c[orden], k[orden]
    mejor = float("inf")
    aciertos = np.cumsum(k) / np.arange(1, len(k) + 1)
    for i in range(len(c)):
        # solo se corta donde cambia la confianza, para que el umbral sea aplicable
        if (i + 1 == len(c) or c[i + 1] < c[i]) and aciertos[i] >= objetivo:
            mejor = float(c[i])
    return mejor


def cobertura_y_acierto(confianza, correcto, t: float):
    c, k = np.asarray(confianza, float), np.asarray(correcto, float)
    m = c >= t
    return (float(m.mean()), float(k[m].mean()) if m.any() else 0.0)


def mcnemar(correcto_a, correcto_b):
    a, b_ = np.asarray(correcto_a, bool), np.asarray(correcto_b, bool)
    b = int((a & ~b_).sum())
    c = int((~a & b_).sum())
    n = b + c
    if n == 0:
        return b, c, 1.0
    p = 2 * sum(comb(n, i) for i in range(min(b, c) + 1)) / 2 ** n
    return b, c, min(1.0, p)
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/test_metricas.py -v` → 6 passed.

- [ ] **Paso 4: commit**

```bash
git add eval/clasificador-embeddings/metricas.py eval/clasificador-embeddings/tests/test_metricas.py
git commit -m "feat(eval): metricas de acierto, ECE, cobertura con acierto minimo y McNemar exacto (AB#100719)"
```

---

### Tarea 6: Modelos A y B con calibración

**Ficheros:**
- Crear: `eval/clasificador-embeddings/modelos.py`, `tests/test_modelos.py`

**Interfaces:**
- Produce:
  - `class Prediccion(NamedTuple): tdn1: str; tdn2: str; conf_tdn1: float` (`tdn2 == ""` si el subtipo no está cubierto).
  - `class ClasificadorLR: fit(X, tdn1: list[str], tdn2: list[str], min_ejemplos=5) -> self; predecir(X) -> list[Prediccion]` (confianza sin calibrar).
  - `class ClasificadorKNN(k=15)` con la misma interfaz.
  - `class Calibrador: fit(conf, correcto) -> self; aplicar(conf) -> np.ndarray` (isotónica sobre la confianza top-1).
  - `calibrar(modelo, X_cal, tdn1_cal) -> Calibrador`.

- [ ] **Paso 1: test (falla)**

`tests/test_modelos.py`:
```python
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from modelos import Calibrador, ClasificadorKNN, ClasificadorLR


def datos(n=60, semilla=0):
    rng = np.random.default_rng(semilla)
    centros = {"AAAA-01": [5, 0, 0], "AAAA-02": [5, 1, 0], "BBBB-01": [0, 5, 0], "CCCC-01": [0, 0, 5]}
    X, t1, t2 = [], [], []
    for et, c in centros.items():
        for _ in range(n):
            X.append(np.array(c) + rng.normal(0, 0.3, 3))
            t1.append(et[:4])
            t2.append(et)
    X = np.array(X, np.float32)
    return X / np.linalg.norm(X, axis=1, keepdims=True), t1, t2


def test_lr_jerarquico_aprende_familias_y_subtipos():
    X, t1, t2 = datos()
    m = ClasificadorLR().fit(X, t1, t2)
    p = m.predecir(X)
    assert np.mean([a.tdn1 == b for a, b in zip(p, t1)]) > 0.95
    assert all(0 <= a.conf_tdn1 <= 1 for a in p)
    assert {a.tdn2 for a in p if a.tdn1 == "BBBB"} == {"BBBB-01"}  # familia con un solo subtipo


def test_subtipo_con_pocos_ejemplos_no_se_cubre():
    X, t1, t2 = datos()
    t2 = list(t2)
    t2[0:57] = [""] * 57  # AAAA-01 se queda con 3 ejemplos
    p = ClasificadorLR().fit(X, t1, t2, min_ejemplos=5).predecir(X[:3])
    assert all(a.tdn2 in ("", "AAAA-02") for a in p)


def test_knn_misma_interfaz():
    X, t1, t2 = datos()
    p = ClasificadorKNN(k=5).fit(X, t1, t2).predecir(X[:10])
    assert all(a.tdn1 == "AAAA" for a in p)


def test_calibrador_monotono_en_rango():
    c = Calibrador().fit([0.2, 0.4, 0.6, 0.8, 0.9], [0, 0, 1, 1, 1])
    out = c.aplicar([0.1, 0.5, 0.95])
    assert all(0 <= v <= 1 for v in out) and out[0] <= out[1] <= out[2]
```

- [ ] **Paso 2: `modelos.py`**

```python
"""Enfoques A (regresión logística jerárquica) y B (kNN) sobre embeddings, con calibración
isotónica de la confianza top-1 de TDN1."""
from __future__ import annotations

from collections import Counter
from typing import NamedTuple

import numpy as np
from sklearn.isotonic import IsotonicRegression
from sklearn.linear_model import LogisticRegression


class Prediccion(NamedTuple):
    tdn1: str
    tdn2: str
    conf_tdn1: float


def _subtipos_cubiertos(tdn1, tdn2, min_ejemplos):
    n = Counter(t for t in tdn2 if t)
    return {t for t, c in n.items() if c >= min_ejemplos}


class ClasificadorLR:
    def __init__(self, C: float = 10.0):
        self.C = C

    def fit(self, X, tdn1, tdn2, min_ejemplos: int = 5):
        n1 = Counter(tdn1)
        m = np.array([n1[t] >= min_ejemplos for t in tdn1])
        self.m1 = LogisticRegression(C=self.C, max_iter=3000).fit(X[m], np.array(tdn1)[m])
        cubiertos = _subtipos_cubiertos(tdn1, tdn2, min_ejemplos)
        self.m2 = {}
        for fam in self.m1.classes_:
            idx = [i for i, (a, b) in enumerate(zip(tdn1, tdn2)) if a == fam and b in cubiertos]
            subt = sorted({tdn2[i] for i in idx})
            if len(subt) == 1:
                self.m2[fam] = subt[0]
            elif len(subt) > 1:
                self.m2[fam] = LogisticRegression(C=self.C, max_iter=3000).fit(X[idx], [tdn2[i] for i in idx])
        return self

    def predecir(self, X):
        P = self.m1.predict_proba(X)
        out = []
        for x, fila in zip(X, P):
            j = int(np.argmax(fila))
            fam = self.m1.classes_[j]
            m2 = self.m2.get(fam, "")
            sub = m2 if isinstance(m2, str) else m2.predict(x[None, :])[0]
            out.append(Prediccion(fam, sub, float(fila[j])))
        return out


class ClasificadorKNN:
    def __init__(self, k: int = 15):
        self.k = k

    def fit(self, X, tdn1, tdn2, min_ejemplos: int = 5):
        self.X = X
        self.t1 = np.array(tdn1)
        cub = _subtipos_cubiertos(tdn1, tdn2, min_ejemplos)
        self.t2 = np.array([t if t in cub else "" for t in tdn2])
        return self

    def predecir(self, X):
        S = X @ self.X.T  # vectores normalizados: coseno
        out = []
        for s in S:
            vec = np.argpartition(-s, self.k)[: self.k]
            pesos = Counter()
            for i in vec:
                pesos[self.t1[i]] += max(float(s[i]), 0.0)
            fam, w = pesos.most_common(1)[0]
            total = sum(pesos.values()) or 1.0
            subs = Counter(self.t2[i] for i in vec if self.t1[i] == fam and self.t2[i])
            out.append(Prediccion(str(fam), subs.most_common(1)[0][0] if subs else "", w / total))
        return out


class Calibrador:
    def fit(self, conf, correcto):
        self.iso = IsotonicRegression(y_min=0.0, y_max=1.0, out_of_bounds="clip").fit(
            np.asarray(conf, float), np.asarray(correcto, float))
        return self

    def aplicar(self, conf):
        return self.iso.predict(np.asarray(conf, float))


def calibrar(modelo, X_cal, tdn1_cal) -> Calibrador:
    p = modelo.predecir(X_cal)
    return Calibrador().fit([a.conf_tdn1 for a in p], [a.tdn1 == t for a, t in zip(p, tdn1_cal)])
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/test_modelos.py -v` → 4 passed.

- [ ] **Paso 4: commit**

```bash
git add eval/clasificador-embeddings/modelos.py eval/clasificador-embeddings/tests/test_modelos.py
git commit -m "feat(eval): clasificadores LR jerarquico y kNN sobre embeddings con calibracion isotonica (AB#100719)"
```

---

### Tarea 7: Evaluación en el golden, híbrido e informe de métricas

**Ficheros:**
- Crear: `eval/clasificador-embeddings/evaluar.py`, `tests/test_evaluar.py`

**Interfaces:**
- Consume: `embeddings.cargar`, `CACHE/inventario.csv`, `CACHE/texto_origen.csv`, `modelos.*`, `metricas.*`, `comun.BASELINE_CSV`, `comun.INFORME_FICHERO_CSV`.
- Produce:
  - `evaluar.hibrido(pred_modelo: Prediccion, conf_cal: float, t: float, gpt_tdn1: str, gpt_tdn2: str) -> tuple[str, str, bool]` devuelve `(tdn1, tdn2, decide_modelo)`.
  - `eval/runs/<AAAAMMDD-HHMMSS>-clasificador-embeddings/resultados.csv` (una fila por documento del golden: rel_path, esperado, GPT, A, B, confianzas calibradas, origen_texto, procedencia).
  - `metricas.json` con O1-O5 en las vistas `todas` y `sin_procedencia`.

- [ ] **Paso 1: test (falla)**

`tests/test_evaluar.py`:
```python
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from evaluar import hibrido
from modelos import Prediccion


def test_decide_el_modelo_si_supera_el_umbral():
    assert hibrido(Prediccion("NOTS", "NOTS-01", 0.99), 0.97, 0.9, "ESIN", "ESIN-34") == ("NOTS", "NOTS-01", True)


def test_decide_gpt_por_debajo_del_umbral():
    assert hibrido(Prediccion("NOTS", "NOTS-01", 0.5), 0.4, 0.9, "ESIN", "ESIN-34") == ("ESIN", "ESIN-34", False)


def test_subtipo_no_cubierto_toma_el_de_gpt_si_coincide_la_familia():
    assert hibrido(Prediccion("NOTS", "", 0.99), 0.95, 0.9, "NOTS", "NOTS-02") == ("NOTS", "NOTS-02", True)
    assert hibrido(Prediccion("NOTS", "", 0.99), 0.95, 0.9, "ESIN", "ESIN-34") == ("NOTS", "", True)
```

- [ ] **Paso 2: `evaluar.py`**

```python
"""Entrena A y B con 'train', calibra y fija umbrales con 'cal', y evalúa en 'golden'
frente al baseline GPT. Escribe resultados.csv y metricas.json en eval/runs/.
Uso: python evaluar.py"""
from __future__ import annotations

import csv
import json
import time
from datetime import datetime

import numpy as np

from comun import BASELINE_CSV, CACHE, EVAL, INFORME_FICHERO_CSV
from embeddings import cargar
from metricas import acierto, cobertura_y_acierto, ece, mcnemar, umbral_para_acierto
from modelos import ClasificadorKNN, ClasificadorLR, Prediccion, calibrar


def hibrido(pred: Prediccion, conf_cal: float, t: float, gpt_tdn1: str, gpt_tdn2: str):
    if conf_cal < t:
        return gpt_tdn1, gpt_tdn2, False
    tdn2 = pred.tdn2 or (gpt_tdn2 if gpt_tdn1 == pred.tdn1 else "")
    return pred.tdn1, tdn2, True


def leer(p):
    return list(csv.DictReader(p.open(encoding="utf-8-sig"), delimiter=";"))


def metricas_vista(filas, clave):
    y1 = [f["exp_tdn1"] for f in filas]
    y2 = [f["exp_tdn2"] for f in filas]
    ok_gpt = [f["gpt_tdn1"] == f["exp_tdn1"] for f in filas]
    conf_gpt = [f["gpt_conf"] for f in filas]
    # El GPT no tiene conjunto de calibración: su umbral se fija sobre el propio golden, así
    # que su cobertura es optimista y se etiqueta como tal en el informe.
    t_gpt = umbral_para_acierto(conf_gpt, ok_gpt, 0.90)
    out = {"n": len(filas), "gpt": {"tdn1": acierto(y1, [f["gpt_tdn1"] for f in filas]),
                                    "tdn2": acierto(y2, [f["gpt_tdn2"] for f in filas]),
                                    "ece_tdn1": ece(conf_gpt, ok_gpt), "umbral_golden_optimista": t_gpt,
                                    "cobertura_optimista": cobertura_y_acierto(conf_gpt, ok_gpt, t_gpt)[0]}}
    for m in ("A", "B"):
        corr = [f[f"{m}_tdn1"] == f["exp_tdn1"] for f in filas]
        conf = [f[f"{m}_conf"] for f in filas]
        t = clave[m]["umbral"]
        cob, acc = cobertura_y_acierto(conf, corr, t)
        h1 = [f[f"H{m}_tdn1"] for f in filas]
        b, c, p = mcnemar([a == e for a, e in zip(h1, y1)], ok_gpt)
        out[m] = {"tdn1": acierto(y1, [f[f"{m}_tdn1"] for f in filas]),
                  "tdn2": acierto(y2, [f[f"{m}_tdn2"] for f in filas]),
                  "ece_tdn1": ece(conf, corr), "umbral_cal": t, "cobertura": cob, "acierto_cubierto": acc,
                  "hibrido_tdn1": acierto(y1, h1), "hibrido_tdn2": acierto(y2, [f[f"H{m}_tdn2"] for f in filas]),
                  "mcnemar_b_c_p": [b, c, p]}
    return out


def main() -> None:
    inv = {r["sha256"]: r for r in leer(CACHE / "inventario.csv")}
    origen = {r["sha256"]: r["origen_texto"] for r in leer(CACHE / "texto_origen.csv")}
    base = {r["rel_path"].lower(): r for r in leer(BASELINE_CSV)}
    proced = {r["filename"].lower() for r in leer(INFORME_FICHERO_CSV) if r["resultado"] == "PROCEDENCIA"}
    shas, X = cargar()
    pos = {h: i for i, h in enumerate(shas)}

    def conjunto(nombre):
        hs = [h for h, r in inv.items() if r["particion"] == nombre and h in pos]
        return hs, X[[pos[h] for h in hs]], [inv[h]["tdn1"] for h in hs], [inv[h]["tdn2"] for h in hs]

    h_tr, X_tr, t1_tr, t2_tr = conjunto("train")
    h_ca, X_ca, t1_ca, _ = conjunto("cal")
    print(f"train {len(h_tr)} | cal {len(h_ca)}")
    modelos, cal, umbral, lat = {}, {}, {}, {}
    for nombre, m in (("A", ClasificadorLR()), ("B", ClasificadorKNN())):
        modelos[nombre] = m.fit(X_tr, t1_tr, t2_tr)
        cal[nombre] = calibrar(m, X_ca, t1_ca)
        p = m.predecir(X_ca)
        conf = cal[nombre].aplicar([a.conf_tdn1 for a in p])
        umbral[nombre] = umbral_para_acierto(conf, [a.tdn1 == t for a, t in zip(p, t1_ca)], 0.90)

    filas = []
    golden = [r for r in inv.values() if r["particion"] == "golden"]
    for r in golden:
        b = base.get(r["rel_path"].lower())
        if b is None:
            continue
        f = {"rel_path": r["rel_path"], "exp_tdn1": r["tdn1"], "exp_tdn2": r["tdn2"],
             "gpt_tdn1": b["predicted_tdn1"], "gpt_tdn2": b["predicted_tdn2"],
             "gpt_conf": float(b["confianza"] or 0), "origen_texto": origen.get(r["sha256"], "sin_texto"),
             "procedencia": r["rel_path"].split("/")[-1].lower() in proced}
        for nombre, m in modelos.items():
            if r["sha256"] in pos:
                t0 = time.perf_counter()
                p = m.predecir(X[[pos[r["sha256"]]]])[0]
                lat.setdefault(nombre, []).append(time.perf_counter() - t0)
                c = float(cal[nombre].aplicar([p.conf_tdn1])[0])
            else:  # sin texto: cuenta como fallo del clasificador nuevo
                p, c = Prediccion("", "", 0.0), 0.0
            f[f"{nombre}_tdn1"], f[f"{nombre}_tdn2"], f[f"{nombre}_conf"] = p.tdn1, p.tdn2, c
            f[f"H{nombre}_tdn1"], f[f"H{nombre}_tdn2"], f[f"H{nombre}_modelo"] = hibrido(
                p, c, umbral[nombre], f["gpt_tdn1"], f["gpt_tdn2"])
        filas.append(f)

    clave = {m: {"umbral": umbral[m]} for m in modelos}
    res = {"golden_evaluados": len(filas), "golden_inventario": len(golden),
           "todas": metricas_vista(filas, clave),
           "sin_procedencia": metricas_vista([f for f in filas if not f["procedencia"]], clave),
           "latencia_p95_s": {m: float(np.percentile(v, 95)) for m, v in lat.items()},
           "origen_texto_golden": {o: sum(f["origen_texto"] == o for f in filas) for o in {f["origen_texto"] for f in filas}}}
    salida = EVAL / "runs" / f"{datetime.now():%Y%m%d-%H%M%S}-clasificador-embeddings"
    salida.mkdir(parents=True)
    with (salida / "resultados.csv").open("w", newline="", encoding="utf-8-sig") as fh:
        w = csv.DictWriter(fh, list(filas[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(filas)
    (salida / "metricas.json").write_text(json.dumps(res, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(res, indent=2, ensure_ascii=False))
    print("escrito en", salida)


if __name__ == "__main__":
    main()
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/ -v` → todos passed.

- [ ] **Paso 4: ejecución real**

Ejecutar: `.venv/Scripts/python evaluar.py`.
Comprobaciones de cordura antes de dar números por buenos:
- `todas.gpt.tdn1` tiene que reproducir el baseline sobre los documentos cruzados: ≈ 0,559 si `golden_evaluados` = 467. Si no cuadra, el cruce por `rel_path` está mal: parar.
- `golden_evaluados` debe ser cercano a 467; explicar la diferencia.

- [ ] **Paso 5: commit**

```bash
git add eval/clasificador-embeddings/evaluar.py eval/clasificador-embeddings/tests/test_evaluar.py
git commit -m "feat(eval): evaluacion de A, B e hibrido en el golden frente al baseline GPT (AB#100719)"
```

---

### Tarea 8: Enfoque C, gpt-4.1-mini con logprobs sobre el golden

Desviación de la especificación, que se señala en el informe: no se reproduce el prompt de producción, que vive en `PromptTemplates` de BD y depende del pipeline. Se usa un prompt compacto con el catálogo TDN1 de `eval/catalogotdn1.json` (código, nombre y descripción). Mide si las logprobs de gpt-4.1-mini dan una confianza calibrada, no su acierto frente al prompt real.

**Ficheros:**
- Crear: `eval/clasificador-embeddings/logprobs_gpt.py`, `tests/test_logprobs.py`

**Interfaces:**
- Consume: `texto.leer_md`, `embeddings.recortar`, `comun.sesion_http`, `comun.CHAT_*`, `metricas.*`, `CACHE/inventario.csv`.
- Produce:
  - `logprobs_gpt.confianza_de(logprobs: list[dict]) -> float`, que devuelve `exp(sum(logprob))` de los tokens generados.
  - `eval/runs/<fecha>-logprobs-gpt/resultados.csv` y `metricas.json` (acierto TDN1, ECE, umbral y cobertura con el umbral fijado en una muestra de 300 documentos de `cal`).

- [ ] **Paso 1: test (falla)**

`tests/test_logprobs.py`:
```python
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from logprobs_gpt import confianza_de


def test_confianza_producto_de_tokens():
    lp = [{"token": "NO", "logprob": math.log(0.9)}, {"token": "TS", "logprob": math.log(0.5)}]
    assert math.isclose(confianza_de(lp), 0.45)


def test_sin_tokens():
    assert confianza_de([]) == 0.0
```

- [ ] **Paso 2: `logprobs_gpt.py`**

```python
"""Enfoque C: gpt-4.1-mini (deployment 'gpt-4o-mini' de DEV) con logprobs, prompt compacto
con el catálogo TDN1. Calibra y fija umbral con 300 documentos de 'cal' y evalúa en 'golden'.
Uso: python logprobs_gpt.py"""
from __future__ import annotations

import csv
import json
import math
import random
import time
from datetime import datetime

from comun import CACHE, CHAT_DEPLOYMENT, CHAT_ENDPOINT, EVAL, sesion_http
from embeddings import recortar
from metricas import acierto, cobertura_y_acierto, ece, umbral_para_acierto
from modelos import Calibrador
from texto import leer_md

API = "2024-10-21"


def confianza_de(logprobs) -> float:
    return math.exp(sum(t["logprob"] for t in logprobs)) if logprobs else 0.0


def prompt_sistema() -> str:
    cat = json.loads((EVAL / "catalogotdn1.json").read_text(encoding="utf-8-sig"))
    lineas = [f"{c['Codigo']}: {c['Nombre']}. {c['Descripcion']}" for c in cat]
    return ("Clasifica el documento en UNA familia documental. Responde SOLO con el código de 4 letras, "
            "sin nada más.\n\nFamilias:\n" + "\n".join(lineas))


def clasificar(s, sistema, md):
    url = f"{CHAT_ENDPOINT}/openai/deployments/{CHAT_DEPLOYMENT}/chat/completions?api-version={API}"
    cuerpo = {"messages": [{"role": "system", "content": sistema},
                           {"role": "user", "content": recortar(md, 40000)}],
              "temperature": 0, "max_tokens": 4, "logprobs": True}
    for _ in range(6):
        r = s.post(url, json=cuerpo, timeout=120)
        if r.status_code == 429:
            time.sleep(int(r.headers.get("retry-after", 10)))
            continue
        r.raise_for_status()
        ch = r.json()["choices"][0]
        return ch["message"]["content"].strip()[:4].upper(), confianza_de(ch["logprobs"]["content"])
    raise RuntimeError("chat: reintentos agotados")


def correr(s, sistema, filas):
    out = []
    for r in filas:
        md = leer_md(r["sha256"])
        cod, conf = clasificar(s, sistema, md) if md else ("", 0.0)
        out.append({"rel_path": r["rel_path"], "exp_tdn1": r["tdn1"], "C_tdn1": cod, "C_conf": conf})
    return out


def main() -> None:
    inv = list(csv.DictReader((CACHE / "inventario.csv").open(encoding="utf-8-sig"), delimiter=";"))
    cal = sorted((r for r in inv if r["particion"] == "cal"), key=lambda r: r["sha256"])
    cal = random.Random(42).sample(cal, min(300, len(cal)))
    golden = [r for r in inv if r["particion"] == "golden"]
    s, sistema = sesion_http(), prompt_sistema()
    rc = correr(s, sistema, cal)
    calib = Calibrador().fit([f["C_conf"] for f in rc], [f["C_tdn1"] == f["exp_tdn1"] for f in rc])
    t = umbral_para_acierto(calib.aplicar([f["C_conf"] for f in rc]), [f["C_tdn1"] == f["exp_tdn1"] for f in rc], 0.90)
    s = sesion_http()
    rg = correr(s, sistema, golden)
    conf = calib.aplicar([f["C_conf"] for f in rg])
    corr = [f["C_tdn1"] == f["exp_tdn1"] for f in rg]
    cob, acc = cobertura_y_acierto(conf, corr, t)
    res = {"n": len(rg), "tdn1": acierto([f["exp_tdn1"] for f in rg], [f["C_tdn1"] for f in rg]),
           "ece_bruta": ece([f["C_conf"] for f in rg], corr), "ece_cal": ece(conf, corr),
           "umbral_cal": t, "cobertura": cob, "acierto_cubierto": acc}
    salida = EVAL / "runs" / f"{datetime.now():%Y%m%d-%H%M%S}-logprobs-gpt"
    salida.mkdir(parents=True)
    with (salida / "resultados.csv").open("w", newline="", encoding="utf-8-sig") as fh:
        w = csv.DictWriter(fh, list(rg[0].keys()), delimiter=";")
        w.writeheader()
        w.writerows(rg)
    (salida / "metricas.json").write_text(json.dumps(res, indent=2), encoding="utf-8")
    print(json.dumps(res, indent=2))


if __name__ == "__main__":
    main()
```

- [ ] **Paso 3: tests en verde**

Ejecutar: `.venv/Scripts/python -m pytest tests/ -v` → todos passed.

- [ ] **Paso 4: prueba de conexión y ejecución**

```bash
.venv/Scripts/python -c "from comun import sesion_http; from logprobs_gpt import clasificar, prompt_sistema; print(clasificar(sesion_http(), prompt_sistema(), 'NOTA SIMPLE INFORMATIVA Registro de la Propiedad'))"
```
Esperado: `('NOTS', <conf>)`. Después: `.venv/Scripts/python logprobs_gpt.py` (~770 llamadas).

- [ ] **Paso 5: commit**

```bash
git add eval/clasificador-embeddings/logprobs_gpt.py eval/clasificador-embeddings/tests/test_logprobs.py
git commit -m "feat(eval): enfoque C con logprobs de gpt-4.1-mini y prompt compacto de catalogo TDN1 (AB#100719)"
```

---

### Tarea 9: Informe, comprobación de cero cambios y cierre

**Ficheros:**
- Crear: `eval/clasificador-embeddings/INFORME.md`

- [ ] **Paso 1: coste real (O5)**

- Tokens de embeddings: suma de la columna 2 de `CACHE/embeddings_uso.csv`.
- Tokens de GPT de fase 1: los del baseline no están en `results.csv`; usar la tabla de costes por ejecución de DEV (AB#100224) si hay datos de la golden del 22/09, o marcar "no medido".
- Precios: tomarlos de la página oficial vigente y citar la fuente en el informe. No inventarlos.

- [ ] **Paso 2: `INFORME.md`**

Contenido obligatorio, con las cifras de `metricas.json` de las tareas 7 y 8:
1. Tabla O1-O5 × {A, B, H-A, H-B, C, GPT baseline} en las dos vistas (`todas`, `sin_procedencia`), con ✅/❌/zona gris según los umbrales de las restricciones globales.
2. Decisión según la regla de §5 de la especificación: **sigue / para / zona gris**, razonada.
3. Reparto de `origen_texto` en el golden y acierto por origen.
4. Desviaciones de la especificación: el prompt de C, los documentos sin texto y el alcance de DI autorizado.
5. Los 10 pares de confusión TDN1 más frecuentes de A (desde `resultados.csv`).
6. Siguiente paso propuesto si la decisión es "sigue" (especificación y PBI nuevos para el modo sombra; no se crea nada).

- [ ] **Paso 3: comprobación de cero cambios en DEV (criterio 4 del PBI)**

```bash
SUB=$(az account list --query "[?starts_with(id,'8764f9ff')].id | [0]" -o tsv)
for a in srbaisrv01devdocai srbaisrv02devdocai; do az cognitiveservices account deployment list -g SRBRGDEVDOCSAI -n $a --subscription "$SUB" --query "[].{d:name,m:properties.model.name,v:properties.model.version,cap:sku.capacity}" -o tsv; done
```
Esperado, idéntico al 2026-09-25:
- `srbaisrv01devdocai`: gpt-4o (gpt-4o 2024-11-20, 250), gpt-4o-mini (gpt-4.1-mini 2025-04-14, 150), gpt-4.1-715420 (150), gpt-4.1-mini-622960 (250), text-embedding-3-large-030358 (150), gpt-5-mini (50).
- `srbaisrv02devdocai`: gpt-4.1-892749 (250), gpt-4.1-mini-590191 (250), text-embedding-3-large-010650 (250).

Pegar el resultado en el informe.

- [ ] **Paso 4: commit y cierre**

```bash
git add eval/clasificador-embeddings/INFORME.md
git commit -m "docs(eval): informe de la prueba de clasificador por embeddings con decision sigue/para (AB#100719)"
```
Traceability Gate para AB#100719: tests (comando y n/n), criterios de aceptación 1-4 uno a uno, impacto documental (especificación + informe). Proponer al usuario, con payload, el cambio de estado del PBI y, si la decisión es "para", el borrado de `C:/temp/MVP/spike-embeddings-cache/`. No ejecutar ninguna de las dos cosas sin su sí.
