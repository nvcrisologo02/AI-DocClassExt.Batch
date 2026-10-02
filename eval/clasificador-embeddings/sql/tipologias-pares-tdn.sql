SET NOCOUNT ON;
-- Pares TDN1/TDN2 de las tipologias publicadas (entrada de exportar_modelo.py, AB#100779).
-- Ejecutar contra DEV (srbsqldevdocai, BD DocumentIA) y guardar la salida como tipologias-dev.json.
-- ConfiguracionJson mezcla camelCase y PascalCase segun la fila: se cubren ambas grafias.
SELECT t.Codigo AS codigo,
       COALESCE(JSON_VALUE(t.ConfiguracionJson, '$.classification.tdn1'),
                JSON_VALUE(t.ConfiguracionJson, '$.Classification.Tdn1'),
                JSON_VALUE(t.ConfiguracionJson, '$.tdn1'),
                JSON_VALUE(t.ConfiguracionJson, '$.Tdn1')) AS tdn1,
       COALESCE(JSON_VALUE(t.ConfiguracionJson, '$.classification.tdn2'),
                JSON_VALUE(t.ConfiguracionJson, '$.Classification.Tdn2'),
                JSON_VALUE(t.ConfiguracionJson, '$.tdn2'),
                JSON_VALUE(t.ConfiguracionJson, '$.Tdn2')) AS tdn2
FROM dbo.Tipologias t
WHERE t.Estado = 1 AND t.Activa = 1          -- EstadoTipologia.Published
  AND ISJSON(t.ConfiguracionJson) = 1
ORDER BY t.Codigo
FOR JSON PATH;
