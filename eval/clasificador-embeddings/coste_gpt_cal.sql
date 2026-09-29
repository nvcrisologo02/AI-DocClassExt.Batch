-- Coste de clasificación GPT del run del GPT de producción sobre cal (AB#100775, INFORME.md §11).
-- Ejecutada el 2026-09-29 contra DEV: srbsqldevdocai.database.windows.net / DocumentIA,
-- solo lectura, con el mecanismo de conexión de texto.py (token de az CLI, sin credenciales).
-- Ventana: la del run eval/runs/20260928-215126-gpt-cal (inicio 2026-09-28 19:51:27 UTC, tras
-- el smoke de 2 documentos; fin 2026-09-29 01:33:27 UTC) y el SubmittedBy de la consola.
-- Resultado del 2026-09-29: 1.562 filas (de 1.576 documentos), TokensIA 39.741.492,
-- CosteClasificacionEur total 9,158974, media 0,005886, mediana 0,004825,
-- ReutilizadaPorDuplicado 0 en todas.

DECLARE @desde datetime2 = '2026-09-28T19:51:26', @hasta datetime2 = '2026-09-29T01:34:00';

SELECT COUNT(*)                                     AS Ejecuciones,
       SUM(CAST(TokensIA AS bigint))                AS TokensIA,
       SUM(CosteClasificacionEur)                   AS CosteClasificacionEurTotal,
       AVG(CosteClasificacionEur)                   AS CosteClasificacionEurMedio,
       SUM(CAST(ReutilizadaPorDuplicado AS int))    AS FilasReutilizadas
FROM DocumentoEjecuciones
WHERE FechaEjecucion >= @desde AND FechaEjecucion <= @hasta
  AND SubmittedBy LIKE 'DocumentIA.Batch.Evaluation/%';

SELECT DISTINCT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY CosteClasificacionEur) OVER () AS CosteClasificacionEurMediana
FROM DocumentoEjecuciones
WHERE FechaEjecucion >= @desde AND FechaEjecucion <= @hasta
  AND SubmittedBy LIKE 'DocumentIA.Batch.Evaluation/%';
