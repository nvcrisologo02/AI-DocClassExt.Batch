-- Coste de clasificación GPT de la golden BASELINE-GPT4OMINI-DEV (AB#100719, INFORME.md §5).
-- Ejecutada el 2026-09-28 contra DEV: srbsqldevdocai.database.windows.net / DocumentIA,
-- solo lectura, con el mecanismo de conexión de texto.py (token de az CLI, sin credenciales).
-- Ventana: la de run-info.json de la golden (2026-09-22 10:12:56-11:35:19 UTC) y el
-- SubmittedBy de la consola de evaluación. Columnas de coste de AB#100224.
-- Resultado del 2026-09-28: 459 filas (de 467 documentos), TokensIA 11.321.259,
-- CosteClasificacionEur total 2,635733, media 0,005742, mediana 0,004597,
-- CosteEstimado 0 en todas, ReutilizadaPorDuplicado 0 en todas.

DECLARE @desde datetime2 = '2026-09-22T10:12:00', @hasta datetime2 = '2026-09-22T11:36:00';

SELECT COUNT(*)                                     AS Ejecuciones,
       SUM(CAST(TokensIA AS bigint))                AS TokensIA,
       SUM(CosteClasificacionEur)                   AS CosteClasificacionEurTotal,
       AVG(CosteClasificacionEur)                   AS CosteClasificacionEurMedio,
       SUM(CAST(CosteEstimado AS int))              AS FilasConCosteEstimado,
       SUM(CAST(ReutilizadaPorDuplicado AS int))    AS FilasReutilizadas
FROM DocumentoEjecuciones
WHERE FechaEjecucion >= @desde AND FechaEjecucion <= @hasta
  AND SubmittedBy LIKE 'DocumentIA.Batch.Evaluation/%';

SELECT DISTINCT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY CosteClasificacionEur) OVER () AS CosteClasificacionEurMediana
FROM DocumentoEjecuciones
WHERE FechaEjecucion >= @desde AND FechaEjecucion <= @hasta
  AND SubmittedBy LIKE 'DocumentIA.Batch.Evaluation/%';
