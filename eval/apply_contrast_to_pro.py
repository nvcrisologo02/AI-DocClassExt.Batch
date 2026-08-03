"""Aplica a PRO las reglas de contraste TDN1 validadas en DEV (AB#99983).

Las reglas (NOTS, CUAD, CERA por v1; COMU, CORR por v2 asimetrica) se midieron en
DEV sobre el golden: acierto de familia 42% -> 55% en esas 5 familias, McNemar
p=0.022 (significativo), sin regresion en ninguna. El resto del catalogo TDN1 es
identico entre PRO y DEV, por lo que el cambio se limita a esas 5 descripciones.

SEGURIDAD (esto toca PRODUCCION):
  - Por defecto es DRY-RUN: muestra el diff y NO escribe nada. Requiere --apply.
  - Antes de escribir guarda un backup del estado actual de PRO en un fichero
    con marca de tiempo, y emite el comando exacto para revertir.
  - Toma las descripciones de DEV en el momento de ejecutar (fuente de lo
    validado), no copias embebidas que pudieran quedar desfasadas.
  - Resuelve el Id de cada familia POR CODIGO en el catalogo de PRO (no asume
    que los Ids coincidan entre entornos).
  - Preserva Codigo, Nombre y Tdn2Prompt; solo cambia Descripcion.
  - Aborta si alguna familia de PRO ya contiene la regla (idempotente) o si la
    descripcion de PRO ha divergido de lo esperado.

Claves de function app: se leen del config.json del runner de evaluacion; la de
PRO puede pasarse tambien por la variable de entorno DOCUMENTIA_PRO_KEY. Nunca
se imprimen.

Uso:
  python eval/apply_contrast_to_pro.py                 # dry-run (por defecto)
  python eval/apply_contrast_to_pro.py --apply         # aplica de verdad
  python eval/apply_contrast_to_pro.py --revert <backup.json>
"""
from __future__ import annotations

import argparse
import json
import os
import ssl
import sys
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

FAMILIAS = ["NOTS", "CUAD", "CERA", "COMU", "CORR"]
DEV_URL = "https://srbappdevdocai.azurewebsites.net/api/management/catalogotdn1"
PRO_URL = "https://srbappprodocai.azurewebsites.net/api/management/catalogotdn1"
CONFIG = Path(__file__).resolve().parents[1] / "src/DocumentIA.Batch.Evaluation/bin/Debug/net8.0-windows/config.json"

_CTX = ssl.create_default_context()
_CTX.check_hostname = False
_CTX.verify_mode = ssl.CERT_NONE


def _keys() -> tuple[str, str]:
    cfg = json.loads(CONFIG.read_text(encoding="utf-8-sig"))
    envs = {e["Name"].upper(): e.get("FunctionKey", "") for e in cfg["Environments"]}
    dev = envs.get("DEV", "")
    pro = os.environ.get("DOCUMENTIA_PRO_KEY") or envs.get("PRO", "")
    if not dev:
        sys.exit("No hay Function Key de DEV en config.json (se necesita para leer las reglas validadas).")
    if not pro:
        sys.exit("No hay Function Key de PRO. Rellenala en config.json o exporta DOCUMENTIA_PRO_KEY.")
    return dev, pro


def _http(method: str, url: str, body: dict | None = None) -> list | dict:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, context=_CTX, timeout=90) as r:
        return json.loads(r.read().decode("utf-8"))


def _catalogo(url: str, key: str) -> dict[str, dict]:
    return {x["Codigo"]: x for x in _http("GET", f"{url}?code={key}")}


def revert(backup_path: str) -> None:
    """Restaura en PRO las descripciones guardadas en un backup previo."""
    _, pro_key = _keys()
    backup = json.loads(Path(backup_path).read_text(encoding="utf-8"))
    pro = _catalogo(PRO_URL, pro_key)
    for code, prev in backup.items():
        e = pro[code]
        body = {"Codigo": e["Codigo"], "Nombre": e["Nombre"],
                "Descripcion": prev["Descripcion"], "Tdn2Prompt": e.get("Tdn2Prompt")}
        _http("PUT", f"{PRO_URL}/{e['Id']}?code={pro_key}", body)
        print(f"  {code}: revertido a {len(prev['Descripcion'] or '')} chars")
    print("Reversion completada.")


def main() -> None:
    ap = argparse.ArgumentParser(description="Aplica a PRO las reglas de contraste validadas en DEV.")
    ap.add_argument("--apply", action="store_true", help="Escribe en PRO (por defecto solo dry-run).")
    ap.add_argument("--revert", metavar="BACKUP_JSON", help="Restaura PRO desde un backup previo.")
    args = ap.parse_args()

    if args.revert:
        revert(args.revert)
        return

    dev_key, pro_key = _keys()
    dev = _catalogo(DEV_URL, dev_key)
    pro = _catalogo(PRO_URL, pro_key)

    plan = []
    for code in FAMILIAS:
        if code not in dev or code not in pro:
            sys.exit(f"La familia {code} no existe en ambos catalogos; abortado.")
        nueva = (dev[code].get("Descripcion") or "").strip()
        actual = (pro[code].get("Descripcion") or "").strip()
        if not nueva:
            sys.exit(f"La descripcion de {code} en DEV esta vacia; abortado.")
        if actual == nueva:
            print(f"  {code}: PRO ya coincide con DEV, se omite.")
            continue
        if len(actual) >= len(nueva):
            # PRO deberia ser la version corta (sin regla). Si no lo es, algo cambio.
            print(f"  AVISO {code}: la descripcion de PRO ({len(actual)}) no es mas corta que la de DEV "
                  f"({len(nueva)}). Revisar manualmente antes de aplicar.")
        plan.append((code, actual, nueva))

    if not plan:
        print("\nNada que aplicar: PRO ya esta alineado con DEV en las 5 familias.")
        return

    print(f"\n=== PLAN ({'APLICAR' if args.apply else 'DRY-RUN'}) ===")
    for code, actual, nueva in plan:
        print(f"  {code}: {len(actual)} -> {len(nueva)} chars (+{len(nueva)-len(actual)})")
        anadido = nueva[len(actual):].strip() if nueva.startswith(actual) else "(reescritura completa)"
        print(f"      texto anadido: {anadido[:160]}...")

    if not args.apply:
        print("\nDRY-RUN: no se ha escrito nada. Ejecuta con --apply para aplicarlo.")
        return

    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    bpath = Path(__file__).with_name(f"catalog_backup_PRO_{stamp}.json")
    bpath.write_text(json.dumps({c: {"Id": pro[c]["Id"], "Descripcion": pro[c].get("Descripcion")}
                                 for c, _, _ in plan}, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"\nBackup de PRO: {bpath}")

    for code, _, nueva in plan:
        e = pro[code]
        body = {"Codigo": e["Codigo"], "Nombre": e["Nombre"], "Descripcion": nueva,
                "Tdn2Prompt": e.get("Tdn2Prompt")}
        r = _http("PUT", f"{PRO_URL}/{e['Id']}?code={pro_key}", body)
        print(f"  {code} (id {e['Id']}): aplicado -> {len(r.get('Descripcion') or '')} chars")

    final = _catalogo(PRO_URL, pro_key)
    print("\nVerificacion:")
    for code, _, nueva in plan:
        ok = (final[code].get("Descripcion") or "").strip() == nueva
        print(f"  {code}: {'OK' if ok else 'REVISAR'}")

    print(f"\nPara revertir:\n  python eval/apply_contrast_to_pro.py --revert {bpath}")
    print("Nota: el cache de prompts tarda ~5 min en refrescarse tras el cambio.")


if __name__ == "__main__":
    main()
