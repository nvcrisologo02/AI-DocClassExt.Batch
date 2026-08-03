/* ============================================================================
 Reglas de contraste TDN1 - aplicacion a PRODUCCION (AB#99983)

 Que hace: actualiza la columna Descripcion de 5 familias del CatalogoTdn1
 (NOTS, CUAD, CERA, COMU, CORR) con las reglas de desempate validadas en DEV.

 Evidencia: medido sobre el golden en DEV, el acierto de familia en esas 5
 familias paso de 42% a 55% (+13 pp), con test de McNemar p=0.022
 (estadisticamente significativo) y sin regresion en ninguna de las cinco.

 Seguridad:
   - Crea primero la tabla de respaldo dbo.CatalogoTdn1_Backup_20260803.
   - Todo va dentro de una transaccion: revisa el SELECT de verificacion
     ANTES de ejecutar el COMMIT. Si algo no cuadra, ejecuta ROLLBACK.
   - Solo modifica Descripcion; no toca Codigo, Nombre ni TDN2_Prompt.
   - Idempotente: si la regla ya esta aplicada, el UPDATE afecta 0 filas.
   - Los saltos de linea se expresan con NCHAR(13)/NCHAR(10) para reproducir
     el texto exacto con independencia de como guarde el fichero el editor.

 Nota operativa: la aplicacion cachea el catalogo unos 5 minutos, por lo que
 el cambio no surte efecto de forma inmediata.

 Reversion: bloque comentado al final del script.
 ============================================================================ */

SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

-- 1) Respaldo del estado actual ----------------------------------------------
IF OBJECT_ID('dbo.CatalogoTdn1_Backup_20260803', 'U') IS NOT NULL
    DROP TABLE dbo.CatalogoTdn1_Backup_20260803;

SELECT Id, Codigo, Nombre, Descripcion, TDN2_Prompt, SYSUTCDATETIME() AS BackupUtc
INTO dbo.CatalogoTdn1_Backup_20260803
FROM dbo.CatalogoTdn1
WHERE Codigo IN ('NOTS','CUAD','CERA','COMU','CORR');

-- 2) Aplicacion de las reglas -------------------------------------------------
-- NOTS --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: nota simple, nota informativa o información de índices del Registro de la Propiedad. Valor puramente INFORMATIVO.' + NCHAR(13) + NCHAR(10)
         + N'CÓMO RECONOCERLA: el documento lleva membrete "Registradores de España / Información Registral expedida por [Registrador]" Y en el cuerpo aparece la expresión "NOTA SIMPLE" (o "nota simple informativa"). Describe finca, titulares, cargas e inscripciones vigentes.'
         + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13) + NCHAR(10) + N'· Documento que dice "CERTIFICO"/"CERTIFICACIÓN" con fe pública -> CERJ' + NCHAR(13)
         + NCHAR(10)
         + N'· Documento que informa del RESULTADO de un trámite registral (calificación, inscripción practicada, suspensión, denegación, asiento de presentación) -> INRG'
         + NCHAR(13) + NCHAR(10)
         + N'DESEMPATE CRÍTICO: el membrete de Registradores es IDÉNTICO en NOTS, CERJ e INRG. NO decidas por él. Si el cuerpo dice "nota simple", es NOTS aunque hable de dominio, cargas o inscripciones. Este es el error más común del sistema.'
         + NCHAR(10) + NCHAR(10)
         + N'INCLUYE los informes de titularidad registral y la informacion de indices del Registro (busqueda por indices/NIF, resultado positivo o negativo), AUNQUE se titule ''Informe'' y hable de ''resultado''. No por ello es CERJ (no certifica con fe publica) ni ESIN (no es informe analitico): es informacion registral de indices -> NOTS.'
WHERE  Codigo = 'NOTS';

-- CUAD --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: cuadro, tabla o planning de cálculo: amortización, reparto de costes, coeficientes, superficies, distribución de responsabilidad hipotecaria, cuadro económico de viabilidad.'
         + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13) + NCHAR(10) + N'· Listado o inventario sin cálculo -> INLI' + NCHAR(13) + NCHAR(10)
         + N'· Presupuesto de costes futuros -> PRES' + NCHAR(13) + NCHAR(10) + N'· Balance o estados financieros -> CULC' + NCHAR(10) + NCHAR(10)
         + N'DESEMPATE: un cuadro/tabla/planning de CALCULO (cuadro de amortizacion con vencimientos/capital/intereses; planning de obra con partidas y reparto de costes) es CUAD aunque contenga cifras. NO es CERJ (no certifica ni acredita, solo calcula) ni PRES (PRES es una estimacion FUTURA de coste; CUAD es la tabla de calculo/reparto en si).'
WHERE  Codigo = 'CUAD';

-- CERA --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: documento que ACREDITA que un pago o cobro SE HA PRODUCIDO: justificante de transferencia o abono, recibo, carta de pago, extracto con el movimiento, autoliquidación puntual de impuesto/tasa, certificado de estar al corriente de pago.'
         + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13) + NCHAR(10) + N'· Factura o minuta que reclama el importe -> FACT' + NCHAR(13) + NCHAR(10)
         + N'· Certificado sin movimiento de dinero -> CERJ' + NCHAR(13) + NCHAR(10) + N'· Declaración periódica de impuestos (IVA, IRPF, sociedades) -> DECL'
         + NCHAR(13) + NCHAR(10) + N'· Comunicación que solo informa sin acreditar pago -> COMU' + NCHAR(13) + NCHAR(10)
         + N'DESEMPATE: la clave es si el DINERO YA SE MOVIÓ. "Se ha recibido una transferencia de X €", "justificante de abono", "recibo", "carta de pago" -> CERA, AUNQUE llegue en forma de aviso o notificación bancaria. "Debe usted abonar" -> COMU o FACT.'
         + NCHAR(10) + NCHAR(10)
         + N'INCLUYE cualquier acreditacion de un pago/cobro YA PRODUCIDO aunque venga con otra forma: carta que remite un cheque con su importe; recibo o carta de pago de un impuesto (IBI, IAE, plusvalia); certificado de importes al corriente. NO es COMU (aunque sea carta), ni DOCA (aunque sea de una administracion), ni FACT (FACT RECLAMA el importe; CERA acredita que se PAGO/COBRO).'
WHERE  Codigo = 'CERA';

-- COMU --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: comunicación, notificación, requerimiento o solicitud cuyo acto principal es INFORMAR o PEDIR: burofaxes, notificaciones al deudor, solicitudes de licencia, comunicaciones de obra, servidumbres, CONSENTIMIENTOS firmados por terceros (protección de datos, consulta de ficheros), autorizaciones de cargo en cuenta.'
         + NCHAR(13) + NCHAR(10) + N'INCLUYE TAMBIÉN:' + NCHAR(13) + NCHAR(10)
         + N'· PBC / SCREENING: resultado de buscar una persona o entidad en listas internas de Sareb, listas de sanciones o bases de datos de prevención de blanqueo -> COMU-51. Aunque el documento se llame "informe de búsqueda", el acto es COMUNICAR el resultado del screening, no analizar: NO es ESIN.'
         + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13) + NCHAR(10) + N'· Documento que acredita un pago realizado -> CERA' + NCHAR(13) + NCHAR(10)
         + N'· Certificado que acredita un hecho -> CERJ' + NCHAR(13) + NCHAR(10) + N'· Contrato o acuerdo firmado -> CNCV' + NCHAR(13) + NCHAR(10)
         + N'· Resolución judicial -> SERE' + NCHAR(13) + NCHAR(10) + N'· Mero correo sin acto sustantivo -> CORR' + NCHAR(13) + NCHAR(10)
         + N'DESEMPATE: un CONSENTIMIENTO de tratamiento de datos firmado por un cliente es COMU (no PRPI). Si el contenido tiene tipología propia (certificado, factura, pago), prima esa familia sobre el soporte de comunicación.'
         + NCHAR(10) + NCHAR(10)
         + N'DESEMPATE COMU/CORR: un email o cadena de correos es COMU SOLO si constituye una comunicacion FORMAL con entidad propia: burofax, notificacion fehaciente, requerimiento formal, comunicacion o escrito dirigido a una administracion o dentro de un procedimiento (p.ej. comunicacion de creditos art. 255 TRLC), consentimiento o autorizacion firmada. Un simple correo de gestion que informa o pide algo de forma operativa NO es COMU: es CORR.'
WHERE  Codigo = 'COMU';

-- CORR --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: correo electrónico, carta postal ordinaria o registro de llamada telefónica usados como MERO SOPORTE de comunicación, sin un acto sustantivo propio dentro. Cadenas de emails de gestión, correos de acompañamiento, notas de llamada.'
         + NCHAR(13) + NCHAR(10)
         + N'CÓMO RECONOCERLO: el documento ES un email (con De/Para/Asunto/Fecha, cadena de respuestas) o la transcripción/nota de una llamada, y su contenido es conversacional o de mera gestión: pedir un dato, confirmar recepción, coordinar una gestión, reenviar información.'
         + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13) + NCHAR(10)
         + N'· Email o carta cuyo contenido ES una notificación formal, requerimiento o solicitud con efecto propio -> COMU' + NCHAR(13) + NCHAR(10)
         + N'· Email que adjunta o transcribe un documento con tipología propia (contrato, factura, certificado) -> la familia de ese contenido' + NCHAR(13)
         + NCHAR(10) + N'· Justificante de pago o cheque -> CERA' + NCHAR(13) + NCHAR(10)
         + N'DESEMPATE con COMU: pregúntate "¿qué acto documenta?". Si la respuesta es "ninguno, es una conversación o una gestión por correo/teléfono" -> CORR. Si hay un acto formal de notificar/requerir/solicitar con efecto -> COMU. El formato de EMAIL o de LLAMADA es la señal principal de CORR.'
         + NCHAR(10) + NCHAR(10)
         + N'DESEMPATE COMU/CORR (el formato email NO decide): POR DEFECTO, una cadena de correos o un correo suelto de gestion es CORR (el correo es el SOPORTE), aunque en su cuerpo se transmita, reenvie, informe o pida algo de forma operativa. Ejemplos que SON CORR: impresion de un correo de Outlook reenviado; cadena de correos de gestion entre partes; correo que acompana o remite un documento. Solo deja de ser CORR si es una comunicacion FORMAL con entidad propia (ver COMU).'
WHERE  Codigo = 'CORR';

-- 3) Verificacion (revisar ANTES de confirmar) --------------------------------
SELECT  c.Codigo,
        LEN(b.Descripcion) AS LongitudAntes,
        LEN(c.Descripcion) AS LongitudDespues,
        CASE WHEN c.Descripcion LIKE N'%DESEMPATE%' OR c.Descripcion LIKE N'%INCLUYE%'
             THEN 'REGLA PRESENTE' ELSE 'REVISAR' END AS Comprobacion
FROM    dbo.CatalogoTdn1 c
JOIN    dbo.CatalogoTdn1_Backup_20260803 b ON b.Codigo = c.Codigo
WHERE   c.Codigo IN ('NOTS','CUAD','CERA','COMU','CORR')
ORDER BY c.Codigo;

-- Si el resultado es correcto:
COMMIT TRANSACTION;
-- Si algo no cuadra, en su lugar:  ROLLBACK TRANSACTION;


/* ============================================================================
 REVERSION (ejecutar solo si hay que deshacer el cambio)

BEGIN TRANSACTION;
UPDATE  c
SET     c.Descripcion = b.Descripcion
FROM    dbo.CatalogoTdn1 c
JOIN    dbo.CatalogoTdn1_Backup_20260803 b ON b.Codigo = c.Codigo;

SELECT  Codigo, LEN(Descripcion) AS LongitudRestaurada
FROM    dbo.CatalogoTdn1
WHERE   Codigo IN ('NOTS','CUAD','CERA','COMU','CORR')
ORDER BY Codigo;
COMMIT TRANSACTION;
============================================================================ */
