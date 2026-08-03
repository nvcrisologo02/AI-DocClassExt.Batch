/* ============================================================================
 Reglas de contraste TDN1 - aplicacion a PRODUCCION (AB#99983)

 QUE HACE
   Actualiza la columna Descripcion de 5 familias del catalogo TDN1
   (NOTS, CUAD, CERA, COMU, CORR) con las reglas de desempate ya validadas.

 EVIDENCIA
   Medido en DEV sobre el corpus de referencia: el acierto de familia en esas
   5 familias paso de 42% a 55% (+13 puntos), con test de McNemar p=0.022
   (estadisticamente significativo) y sin retroceso en ninguna de las cinco.

 COMO EJECUTARLO EN SQL SERVER MANAGEMENT STUDIO
   1. Conectate al servidor de PRODUCCION y selecciona la base DocumentIA.
   2. Ejecuta el script completo (F5). Se quedara dentro de una transaccion
      ABIERTA y mostrara una tabla de verificacion.
   3. Revisa esa tabla: LongitudDespues debe ser mayor que LongitudAntes y
      la columna Comprobacion debe decir 'REGLA PRESENTE' en las 5 filas.
   4. Si todo es correcto, ejecuta a mano:   COMMIT TRANSACTION;
      Si algo no cuadra, ejecuta a mano:     ROLLBACK TRANSACTION;

 IMPORTANTE: mientras no ejecutes COMMIT o ROLLBACK la transaccion sigue
 abierta y la tabla queda bloqueada para otras sesiones. No dejes la ventana
 a medias.

 SEGURIDAD
   - Crea primero la tabla de respaldo dbo.CatalogoTdn1_Backup_20260803.
   - Solo modifica Descripcion; no toca Codigo, Nombre ni TDN2_Prompt.
   - El texto se emite en ASCII puro (los acentos y saltos de linea van como
     NCHAR), por lo que el resultado no depende de la codificacion del fichero.
   - Al final hay un bloque de REVERSION comentado.

 Nota: la aplicacion cachea el catalogo unos 5 minutos; el cambio no surte
 efecto de forma inmediata.
 ============================================================================ */

USE DocumentIA;
GO

SET XACT_ABORT ON;
SET NOCOUNT ON;
GO

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
SET    Descripcion = N'ES: nota simple, nota informativa o informaci' + NCHAR(243) + N'n de ' + NCHAR(237)
       + N'ndices del Registro de la Propiedad. Valor puramente INFORMATIVO.' + NCHAR(13) + NCHAR(10) + N'C' + NCHAR(211)
       + N'MO RECONOCERLA: el documento lleva membrete "Registradores de Espa' + NCHAR(241) + N'a / Informaci' + NCHAR(243)
       + N'n Registral expedida por [Registrador]" Y en el cuerpo aparece la expresi' + NCHAR(243)
       + N'n "NOTA SIMPLE" (o "nota simple informativa"). Describe finca, titulares, cargas e inscripciones vigentes.' + NCHAR(13) + NCHAR(10)
       + N'NO ES:' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Documento que dice "CERTIFICO"/"CERTIFICACI' + NCHAR(211) + N'N" con fe p' + NCHAR(250)
       + N'blica -> CERJ' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Documento que informa del RESULTADO de un tr' + NCHAR(225)
       + N'mite registral (calificaci' + NCHAR(243) + N'n, inscripci' + NCHAR(243) + N'n practicada, suspensi' + NCHAR(243) + N'n, denegaci'
       + NCHAR(243) + N'n, asiento de presentaci' + NCHAR(243) + N'n) -> INRG' + NCHAR(13) + NCHAR(10) + N'DESEMPATE CR' + NCHAR(205)
       + N'TICO: el membrete de Registradores es ID' + NCHAR(201) + N'NTICO en NOTS, CERJ e INRG. NO decidas por ' + NCHAR(233)
       + N'l. Si el cuerpo dice "nota simple", es NOTS aunque hable de dominio, cargas o inscripciones. Este es el error m' + NCHAR(225) + N's com'
       + NCHAR(250) + N'n del sistema.' + NCHAR(10) + NCHAR(10)
       + N'INCLUYE los informes de titularidad registral y la informacion de indices del Registro (busqueda por indices/NIF, resultado positivo o negativo), AUNQUE se titule ''Informe'' y hable de ''resultado''. No por ello es CERJ (no certifica con fe publica) ni ESIN (no es informe analitico): es informacion registral de indices -> NOTS.'
WHERE  Codigo = 'NOTS';

-- CUAD --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: cuadro, tabla o planning de c' + NCHAR(225) + N'lculo: amortizaci' + NCHAR(243)
       + N'n, reparto de costes, coeficientes, superficies, distribuci' + NCHAR(243) + N'n de responsabilidad hipotecaria, cuadro econ' + NCHAR(243)
       + N'mico de viabilidad.' + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Listado o inventario sin c'
       + NCHAR(225) + N'lculo -> INLI' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Presupuesto de costes futuros -> PRES' + NCHAR(13) + NCHAR(10)
       + NCHAR(183) + N' Balance o estados financieros -> CULC' + NCHAR(10) + NCHAR(10)
       + N'DESEMPATE: un cuadro/tabla/planning de CALCULO (cuadro de amortizacion con vencimientos/capital/intereses; planning de obra con partidas y reparto de costes) es CUAD aunque contenga cifras. NO es CERJ (no certifica ni acredita, solo calcula) ni PRES (PRES es una estimacion FUTURA de coste; CUAD es la tabla de calculo/reparto en si).'
WHERE  Codigo = 'CUAD';

-- CERA --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: documento que ACREDITA que un pago o cobro SE HA PRODUCIDO: justificante de transferencia o abono, recibo, carta de pago, extracto con el movimiento, autoliquidaci'
       + NCHAR(243) + N'n puntual de impuesto/tasa, certificado de estar al corriente de pago.' + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13)
       + NCHAR(10) + NCHAR(183) + N' Factura o minuta que reclama el importe -> FACT' + NCHAR(13) + NCHAR(10) + NCHAR(183)
       + N' Certificado sin movimiento de dinero -> CERJ' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Declaraci' + NCHAR(243) + N'n peri' + NCHAR(243)
       + N'dica de impuestos (IVA, IRPF, sociedades) -> DECL' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Comunicaci' + NCHAR(243)
       + N'n que solo informa sin acreditar pago -> COMU' + NCHAR(13) + NCHAR(10) + N'DESEMPATE: la clave es si el DINERO YA SE MOVI' + NCHAR(211)
       + N'. "Se ha recibido una transferencia de X ' + NCHAR(8364)
       + N'", "justificante de abono", "recibo", "carta de pago" -> CERA, AUNQUE llegue en forma de aviso o notificaci' + NCHAR(243)
       + N'n bancaria. "Debe usted abonar" -> COMU o FACT.' + NCHAR(10) + NCHAR(10)
       + N'INCLUYE cualquier acreditacion de un pago/cobro YA PRODUCIDO aunque venga con otra forma: carta que remite un cheque con su importe; recibo o carta de pago de un impuesto (IBI, IAE, plusvalia); certificado de importes al corriente. NO es COMU (aunque sea carta), ni DOCA (aunque sea de una administracion), ni FACT (FACT RECLAMA el importe; CERA acredita que se PAGO/COBRO).'
WHERE  Codigo = 'CERA';

-- COMU --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: comunicaci' + NCHAR(243) + N'n, notificaci' + NCHAR(243)
       + N'n, requerimiento o solicitud cuyo acto principal es INFORMAR o PEDIR: burofaxes, notificaciones al deudor, solicitudes de licencia, comunicaciones de obra, servidumbres, CONSENTIMIENTOS firmados por terceros (protecci'
       + NCHAR(243) + N'n de datos, consulta de ficheros), autorizaciones de cargo en cuenta.' + NCHAR(13) + NCHAR(10) + N'INCLUYE TAMBI'
       + NCHAR(201) + N'N:' + NCHAR(13) + NCHAR(10) + NCHAR(183)
       + N' PBC / SCREENING: resultado de buscar una persona o entidad en listas internas de Sareb, listas de sanciones o bases de datos de prevenci'
       + NCHAR(243) + N'n de blanqueo -> COMU-51. Aunque el documento se llame "informe de b' + NCHAR(250)
       + N'squeda", el acto es COMUNICAR el resultado del screening, no analizar: NO es ESIN.' + NCHAR(13) + NCHAR(10) + N'NO ES:' + NCHAR(13)
       + NCHAR(10) + NCHAR(183) + N' Documento que acredita un pago realizado -> CERA' + NCHAR(13) + NCHAR(10) + NCHAR(183)
       + N' Certificado que acredita un hecho -> CERJ' + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Contrato o acuerdo firmado -> CNCV' + NCHAR(13)
       + NCHAR(10) + NCHAR(183) + N' Resoluci' + NCHAR(243) + N'n judicial -> SERE' + NCHAR(13) + NCHAR(10) + NCHAR(183)
       + N' Mero correo sin acto sustantivo -> CORR' + NCHAR(13) + NCHAR(10)
       + N'DESEMPATE: un CONSENTIMIENTO de tratamiento de datos firmado por un cliente es COMU (no PRPI). Si el contenido tiene tipolog' + NCHAR(237)
       + N'a propia (certificado, factura, pago), prima esa familia sobre el soporte de comunicaci' + NCHAR(243) + N'n.' + NCHAR(10) + NCHAR(10)
       + N'DESEMPATE COMU/CORR: un email o cadena de correos es COMU SOLO si constituye una comunicacion FORMAL con entidad propia: burofax, notificacion fehaciente, requerimiento formal, comunicacion o escrito dirigido a una administracion o dentro de un procedimiento (p.ej. comunicacion de creditos art. 255 TRLC), consentimiento o autorizacion firmada. Un simple correo de gestion que informa o pide algo de forma operativa NO es COMU: es CORR.'
WHERE  Codigo = 'COMU';

-- CORR --------------------------------------------------------------
UPDATE dbo.CatalogoTdn1
SET    Descripcion = N'ES: correo electr' + NCHAR(243) + N'nico, carta postal ordinaria o registro de llamada telef' + NCHAR(243)
       + N'nica usados como MERO SOPORTE de comunicaci' + NCHAR(243) + N'n, sin un acto sustantivo propio dentro. Cadenas de emails de gesti'
       + NCHAR(243) + N'n, correos de acompa' + NCHAR(241) + N'amiento, notas de llamada.' + NCHAR(13) + NCHAR(10) + N'C' + NCHAR(211)
       + N'MO RECONOCERLO: el documento ES un email (con De/Para/Asunto/Fecha, cadena de respuestas) o la transcripci' + NCHAR(243)
       + N'n/nota de una llamada, y su contenido es conversacional o de mera gesti' + NCHAR(243) + N'n: pedir un dato, confirmar recepci'
       + NCHAR(243) + N'n, coordinar una gesti' + NCHAR(243) + N'n, reenviar informaci' + NCHAR(243) + N'n.' + NCHAR(13) + NCHAR(10) + N'NO ES:'
       + NCHAR(13) + NCHAR(10) + NCHAR(183) + N' Email o carta cuyo contenido ES una notificaci' + NCHAR(243)
       + N'n formal, requerimiento o solicitud con efecto propio -> COMU' + NCHAR(13) + NCHAR(10) + NCHAR(183)
       + N' Email que adjunta o transcribe un documento con tipolog' + NCHAR(237)
       + N'a propia (contrato, factura, certificado) -> la familia de ese contenido' + NCHAR(13) + NCHAR(10) + NCHAR(183)
       + N' Justificante de pago o cheque -> CERA' + NCHAR(13) + NCHAR(10) + N'DESEMPATE con COMU: preg' + NCHAR(250) + N'ntate "' + NCHAR(191)
       + N'qu' + NCHAR(233) + N' acto documenta?". Si la respuesta es "ninguno, es una conversaci' + NCHAR(243) + N'n o una gesti' + NCHAR(243)
       + N'n por correo/tel' + NCHAR(233)
       + N'fono" -> CORR. Si hay un acto formal de notificar/requerir/solicitar con efecto -> COMU. El formato de EMAIL o de LLAMADA es la se'
       + NCHAR(241) + N'al principal de CORR.' + NCHAR(10) + NCHAR(10)
       + N'DESEMPATE COMU/CORR (el formato email NO decide): POR DEFECTO, una cadena de correos o un correo suelto de gestion es CORR (el correo es el SOPORTE), aunque en su cuerpo se transmita, reenvie, informe o pida algo de forma operativa. Ejemplos que SON CORR: impresion de un correo de Outlook reenviado; cadena de correos de gestion entre partes; correo que acompana o remite un documento. Solo deja de ser CORR si es una comunicacion FORMAL con entidad propia (ver COMU).'
WHERE  Codigo = 'CORR';

-- 3) Verificacion: revisar ESTA tabla antes de confirmar ----------------------
SELECT  c.Codigo,
        LEN(b.Descripcion) AS LongitudAntes,
        LEN(c.Descripcion) AS LongitudDespues,
        CASE WHEN c.Descripcion LIKE N'%DESEMPATE%' OR c.Descripcion LIKE N'%INCLUYE%'
             THEN 'REGLA PRESENTE' ELSE 'REVISAR' END AS Comprobacion
FROM    dbo.CatalogoTdn1 c
JOIN    dbo.CatalogoTdn1_Backup_20260803 b ON b.Codigo = c.Codigo
WHERE   c.Codigo IN ('NOTS','CUAD','CERA','COMU','CORR')
ORDER BY c.Codigo;

-- 4) Ejecuta A MANO una de estas dos lineas segun el resultado anterior:
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;


/* ============================================================================
 REVERSION (solo si hay que deshacer el cambio despues de haber confirmado)

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
