# tests/test_inventario_manifiesto.py
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inventario_manifiesto import (combinar, dedup_por_hash, etiqueta_desde_carpeta,
                                   particionar_v2, recorrer)

def test_etiqueta_con_sufijo_y_sin_el():
    assert etiqueta_desde_carpeta("CERT-05-Etiqueta") == ("CERT", "CERT-05")
    assert etiqueta_desde_carpeta("ESIN-AI") == ("ESIN", "ESIN-AI")
    assert etiqueta_desde_carpeta("para embeddings") is None

def test_recorrer_aplica_exclusiones_y_extensiones(tmp_path):
    for rel in ["NOTS-01/NOTS-01_1.PDF", "CERT-05-Etiqueta/x.pdf", "ESIN-AI/a.pdf",
                "LIPR-06/LIPR-06_3.PDF", "DOCA-39/d.png", "COMU-66/c.docx", "otra/z.pdf"]:
        p = tmp_path / rel; p.parent.mkdir(exist_ok=True); p.write_bytes(rel.encode())
    filas, exc = recorrer(tmp_path)
    assert {f["rel_path"] for f in filas} == {"NOTS-01/NOTS-01_1.PDF", "CERT-05-Etiqueta/x.pdf", "DOCA-39/d.png"}
    assert next(f for f in filas if f["rel_path"].startswith("CERT"))["tdn2"] == "CERT-05"
    assert all(f["paginas"] == -1 for f in filas)  # bytes que no son PDF: entran igual
    assert {e["motivo"] for e in exc} == {"carpeta_excluida", "fichero_excluido", "extension", "carpeta_invalida"}

def test_dedup_misma_etiqueta_conserva_una_y_conflicto_excluye_todas():
    f = lambda rel, t2, h: {"rel_path": rel, "tdn1": t2[:4], "tdn2": t2, "sha256": h, "paginas": 1, "bytes": 1}
    filas, exc = dedup_por_hash([f("A/2.pdf", "AAAA-01", "h1"), f("A/1.pdf", "AAAA-01", "h1"),
                                 f("B/1.pdf", "BBBB-01", "h2"), f("C/1.pdf", "CCCC-01", "h2")])
    assert [x["rel_path"] for x in filas] == ["A/1.pdf"]
    assert sorted(e["motivo"] for e in exc) == ["conflicto_hash", "conflicto_hash", "duplicado"]

def test_particion_holdout_solo_en_clases_grandes_y_bajo_minimo_fuera():
    filas = [{"sha256": f"a{i}", "tdn1": "AAAA", "tdn2": "AAAA-01"} for i in range(20)]
    filas += [{"sha256": f"b{i}", "tdn1": "BBBB", "tdn2": "BBBB-01"} for i in range(7)]
    filas += [{"sha256": f"c{i}", "tdn1": "CCCC", "tdn2": "CCCC-01"} for i in range(4)]
    out, exc = particionar_v2(filas, semilla=42)
    assert sum(f["particion"] == "hold2" for f in out if f["tdn2"] == "AAAA-01") == 4
    assert all(f["particion"] == "train" for f in out if f["tdn2"] == "BBBB-01")
    assert {e["motivo"] for e in exc} == {"bajo_minimo"} and len(exc) == 4
    assert particionar_v2(filas, semilla=42) == (out, exc)

def test_combinar_no_toca_v1():
    v1 = [{"sha256": "h1", "rel_path": "NOTS/x.pdf", "tdn1": "NOTS", "tdn2": "NOTS-01", "particion": "cal", "paginas": "3"}]
    nuevas = [{"sha256": "h2", "rel_path": "NOTS-01/NOTS-01_1.pdf", "tdn1": "NOTS", "tdn2": "NOTS-01", "particion": "train", "paginas": 2}]
    out = combinar(v1, nuevas, "share-2026-11-03")
    assert out[0] == {**v1[0], "origen": "corpus-v1"}
    assert out[1]["origen"] == "share-2026-11-03"
    assert list(out[0].keys()) == ["sha256", "rel_path", "tdn1", "tdn2", "particion", "paginas", "origen"]


def test_combinar_conserva_origen_de_la_base():
    base = [{"sha256": "h1", "rel_path": "a", "tdn1": "NOTS", "tdn2": "NOTS-01", "particion": "train", "paginas": "1", "origen": "corpus-v1"},
            {"sha256": "h2", "rel_path": "b", "tdn1": "NOTS", "tdn2": "NOTS-01", "particion": "train", "paginas": "1", "origen": "share-2026-11-03"}]
    nuevas = [{"sha256": "h3", "rel_path": "c", "tdn1": "NOTS", "tdn2": "NOTS-01", "particion": "train", "paginas": 2}]
    out = combinar(base, nuevas, "share-2027-01-01")
    assert [f["origen"] for f in out] == ["corpus-v1", "share-2026-11-03", "share-2027-01-01"]
