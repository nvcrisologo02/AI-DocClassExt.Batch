"""Aplica las reglas de contraste a CatalogoTdn1 en DEV via admin API (AB#99983).
Preserva Codigo/Nombre/Tdn2Prompt; solo AMPLIA Descripcion. Backup previo reversible.
La key se lee del config de eval (no se imprime).
"""
import json, os, urllib.request, urllib.error, ssl
from datetime import datetime, timezone

CFG = r"C:/temp/MVP/DocumentIA.Batch/src/DocumentIA.Batch.Evaluation/bin/Debug/net8.0-windows/config.json"
BASE = "https://srbappdevdocai.azurewebsites.net/api/management/catalogotdn1"
cfg = json.load(open(CFG, encoding="utf-8-sig"))
KEY = cfg["Environments"][0]["FunctionKey"]
ctx = ssl.create_default_context(); ctx.check_hostname=False; ctx.verify_mode=ssl.CERT_NONE

APPEND = {
 "CORR": "\n\nDESEMPATE CRITICO COMU/CORR: el formato email NO decide (muchos COMU y CORR son cadenas de correo). Es CORR solo si el correo es MERO SOPORTE: reenvio, acompanamiento o transmision operativa sin acto formal propio (si quitas el correo y su adjunto, no queda un acto sustantivo). Ejemplos que SON CORR: impresion de un correo de Outlook reenviado como soporte; cadena de correos reenviados para transmitir un documento.",
 "COMU": "\n\nDESEMPATE CRITICO COMU/CORR: el formato email NO decide. Aunque sea una cadena de correos, es COMU si el CUERPO del correo ES el acto: una parte (despacho, proveedor, administracion, Sareb) INFORMA, NOTIFICA, REQUIERE o SOLICITA formalmente algo. Ejemplos que SON COMU: cadena en la que un despacho informa al gestor del importe actualizado de la deuda; cadena entre proveedores y Sareb informando y solicitando confirmacion de una incidencia; escrito de comunicacion de creditos (art. 255 TRLC) a la Administracion Concursal.",
 "CUAD": "\n\nDESEMPATE: un cuadro/tabla/planning de CALCULO (cuadro de amortizacion con vencimientos/capital/intereses; planning de obra con partidas y reparto de costes) es CUAD aunque contenga cifras. NO es CERJ (no certifica ni acredita, solo calcula) ni PRES (PRES es una estimacion FUTURA de coste; CUAD es la tabla de calculo/reparto en si).",
 "NOTS": "\n\nINCLUYE los informes de titularidad registral y la informacion de indices del Registro (busqueda por indices/NIF, resultado positivo o negativo), AUNQUE se titule 'Informe' y hable de 'resultado'. No por ello es CERJ (no certifica con fe publica) ni ESIN (no es informe analitico): es informacion registral de indices -> NOTS.",
 "CERA": "\n\nINCLUYE cualquier acreditacion de un pago/cobro YA PRODUCIDO aunque venga con otra forma: carta que remite un cheque con su importe; recibo o carta de pago de un impuesto (IBI, IAE, plusvalia); certificado de importes al corriente. NO es COMU (aunque sea carta), ni DOCA (aunque sea de una administracion), ni FACT (FACT RECLAMA el importe; CERA acredita que se PAGO/COBRO).",
}

def http(method, url, body=None):
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, context=ctx, timeout=60) as r:
        return json.loads(r.read().decode("utf-8"))

# 1) GET catalogo actual
cat = http("GET", f"{BASE}?code={KEY}")
by = {x["Codigo"]: x for x in cat}
targets = ["COMU", "CORR", "CUAD", "NOTS", "CERA"]

# 2) backup reversible
backup = {c: by[c] for c in targets}
bpath = r"C:/temp/MVP/DocumentIA.Batch/eval/catalog_backup_contraste.json"
json.dump(backup, open(bpath, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("backup guardado:", bpath)

# 3) PUT cada una (reemplazo completo, preservando campos)
for c in targets:
    e = by[c]
    if APPEND[c].strip() in (e.get("Descripcion") or ""):
        print(f"{c}: ya contiene el contraste, se omite"); continue
    nueva = (e.get("Descripcion") or "") + APPEND[c]
    body = {"Codigo": e["Codigo"], "Nombre": e["Nombre"], "Descripcion": nueva,
            "Tdn2Prompt": e.get("Tdn2Prompt")}
    r = http("PUT", f"{BASE}/{e['Id']}?code={KEY}", body)
    print(f"{c} (id {e['Id']}): PUT OK, Descripcion {len(e.get('Descripcion') or '')} -> {len(r.get('Descripcion') or '')} chars")

# 4) verificar
cat2 = http("GET", f"{BASE}?code={KEY}")
by2 = {x["Codigo"]: x for x in cat2}
print("\nVerificacion (contraste presente?):")
for c in targets:
    ok = APPEND[c].strip()[:30] in (by2[c].get("Descripcion") or "")
    print(f"  {c}: {'OK' if ok else 'FALTA'}")
