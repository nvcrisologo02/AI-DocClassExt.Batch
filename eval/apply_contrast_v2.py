"""v2 de la regla COMU/CORR (AB#99983): revierte COMU/CORR a su texto ORIGINAL
(desde el backup) y aplica una regla ASIMETRICA que sesga los emails de gestion a
CORR y reserva COMU para comunicaciones formales. NOTS/CUAD/CERA no se tocan (v1 ok).
"""
import json, urllib.request, ssl

CFG = r"C:/temp/MVP/DocumentIA.Batch/src/DocumentIA.Batch.Evaluation/bin/Debug/net8.0-windows/config.json"
BASE = "https://srbappdevdocai.azurewebsites.net/api/management/catalogotdn1"
BACKUP = r"C:/temp/MVP/DocumentIA.Batch/eval/catalog_backup_contraste.json"
cfg = json.load(open(CFG, encoding="utf-8-sig"))
KEY = cfg["Environments"][0]["FunctionKey"]
backup = json.load(open(BACKUP, encoding="utf-8"))
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE

V2 = {
 "CORR": "\n\nDESEMPATE COMU/CORR (el formato email NO decide): POR DEFECTO, una cadena de correos o un correo suelto de gestion es CORR (el correo es el SOPORTE), aunque en su cuerpo se transmita, reenvie, informe o pida algo de forma operativa. Ejemplos que SON CORR: impresion de un correo de Outlook reenviado; cadena de correos de gestion entre partes; correo que acompana o remite un documento. Solo deja de ser CORR si es una comunicacion FORMAL con entidad propia (ver COMU).",
 "COMU": "\n\nDESEMPATE COMU/CORR: un email o cadena de correos es COMU SOLO si constituye una comunicacion FORMAL con entidad propia: burofax, notificacion fehaciente, requerimiento formal, comunicacion o escrito dirigido a una administracion o dentro de un procedimiento (p.ej. comunicacion de creditos art. 255 TRLC), consentimiento o autorizacion firmada. Un simple correo de gestion que informa o pide algo de forma operativa NO es COMU: es CORR.",
}

def http(method, url, body=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={"Content-Type":"application/json"})
    with urllib.request.urlopen(req, context=ctx, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))

cat = http("GET", f"{BASE}?code={KEY}")
by = {x["Codigo"]: x for x in cat}
for c in ("CORR", "COMU"):
    orig = backup[c]["Descripcion"] or ""      # texto original (sin v1)
    nueva = orig + V2[c]
    e = by[c]
    body = {"Codigo": e["Codigo"], "Nombre": e["Nombre"], "Descripcion": nueva, "Tdn2Prompt": e.get("Tdn2Prompt")}
    r = http("PUT", f"{BASE}/{e['Id']}?code={KEY}", body)
    print(f"{c} (id {e['Id']}): v2 aplicada, {len(e.get('Descripcion') or '')} -> {len(r.get('Descripcion') or '')} chars")

cat2 = http("GET", f"{BASE}?code={KEY}")
by2 = {x["Codigo"]: x for x in cat2}
for c in ("CORR", "COMU"):
    ok = "POR DEFECTO" in (by2[c].get("Descripcion") or "") if c=="CORR" else "SOLO si constituye" in (by2[c].get("Descripcion") or "")
    print(f"  verif {c}: {'OK' if ok else 'FALTA'}")
