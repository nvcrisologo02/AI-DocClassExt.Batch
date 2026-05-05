using Microsoft.Data.Sqlite;
using Dapper;
using DocumentIA.Batch.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocumentIA.Batch.Services;

/// <summary>
/// Servicio para persistencia y consulta del historial de ejecuciones batch en SQLite.
/// La BD actúa como fuente de verdad primaria.
/// </summary>
public class BatchHistorialService
{
    private readonly string _dbPath;
    private const string ConnectionStringTemplate = "Data Source={0}";

    public BatchHistorialService()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "historial");
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, "historial.db");
    }

    /// <summary>
    /// Inicializa la base de datos: crea las tablas si no existen.
    /// Se debe llamar en el startup de la aplicación.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(
                string.Format(ConnectionStringTemplate, _dbPath));
            await connection.OpenAsync();

            // Crear tabla BatchRun
            const string createRunTableSql = @"
                CREATE TABLE IF NOT EXISTS BatchRun (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RunKey TEXT NOT NULL UNIQUE,
                    RunFolderPath TEXT NOT NULL,
                    OperationName TEXT NOT NULL,
                    Tipologia TEXT NOT NULL,
                    NumeroColas INTEGER NOT NULL,
                    UmbralConfianza INTEGER NOT NULL,
                    SubirAGdc INTEGER NOT NULL,
                    EjecutarConAssetResolver INTEGER NOT NULL,
                    TotalFiles INTEGER NOT NULL,
                    CompletedFiles INTEGER NOT NULL,
                    RevisionFiles INTEGER NOT NULL,
                    ErrorFiles INTEGER NOT NULL,
                    CanceledFiles INTEGER NOT NULL,
                    SuccessRate TEXT,
                    AverageConfidence TEXT,
                    AverageDuration TEXT,
                    TotalDuration TEXT,
                    ProcessStatus TEXT,
                    CreatedAt TEXT NOT NULL
                )";

            await connection.ExecuteAsync(createRunTableSql);

            // Crear tabla BatchRunFile
            const string createFileTableSql = @"
                CREATE TABLE IF NOT EXISTS BatchRunFile (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RunId INTEGER NOT NULL REFERENCES BatchRun(Id),
                    FileName TEXT NOT NULL,
                    FullPath TEXT NOT NULL,
                    SizeBytes INTEGER NOT NULL,
                    Estado TEXT NOT NULL,
                    InstanceId TEXT,
                    CorrelationId TEXT,
                    RuntimeStatus TEXT,
                    EstadoCalidad TEXT,
                    ConfianzaGlobal REAL,
                    MensajeError TEXT,
                    FechaInicio TEXT,
                    FechaFin TEXT,
                    OutputJsonPath TEXT
                )";

            await connection.ExecuteAsync(createFileTableSql);
        }
        catch (Exception ex)
        {
            // Log pero no interrumpe el startup
            System.Diagnostics.Debug.WriteLine($"[BatchHistorialService] Error en InitializeAsync: {ex.Message}");
        }
    }

    /// <summary>
    /// Guarda una ejecución batch y todos sus archivos de forma atómica.
    /// </summary>
    public async Task SaveRunAsync(
        string runKey,
        string runFolderPath,
        BatchRunSummary summary,
        IReadOnlyList<BatchFileItem> files,
        int umbralConfianza,
        bool subirAGdc,
        bool ejecutarConAssetResolver)
    {
        try
        {
            await using var connection = new SqliteConnection(
                string.Format(ConnectionStringTemplate, _dbPath));
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Insertar BatchRun
                const string insertRunSql = @"
                    INSERT INTO BatchRun (
                        RunKey, RunFolderPath, OperationName, Tipologia,
                        NumeroColas, UmbralConfianza, SubirAGdc, EjecutarConAssetResolver,
                        TotalFiles, CompletedFiles, RevisionFiles, ErrorFiles, CanceledFiles,
                        SuccessRate, AverageConfidence, AverageDuration, TotalDuration,
                        ProcessStatus, CreatedAt
                    ) VALUES (
                        @RunKey, @RunFolderPath, @OperationName, @Tipologia,
                        @NumeroColas, @UmbralConfianza, @SubirAGdc, @EjecutarConAssetResolver,
                        @TotalFiles, @CompletedFiles, @RevisionFiles, @ErrorFiles, @CanceledFiles,
                        @SuccessRate, @AverageConfidence, @AverageDuration, @TotalDuration,
                        @ProcessStatus, @CreatedAt
                    )";

                var runId = await connection.ExecuteScalarAsync<int>(
                    insertRunSql,
                    new
                    {
                        RunKey = runKey,
                        RunFolderPath = runFolderPath,
                        OperationName = summary.OperationName,
                        Tipologia = summary.Tipologia,
                        NumeroColas = summary.NumeroColas,
                        UmbralConfianza = umbralConfianza,
                        SubirAGdc = subirAGdc ? 1 : 0,
                        EjecutarConAssetResolver = ejecutarConAssetResolver ? 1 : 0,
                        TotalFiles = summary.TotalFiles,
                        CompletedFiles = summary.CompletedFiles,
                        RevisionFiles = summary.RevisionFiles,
                        ErrorFiles = summary.ErrorFiles,
                        CanceledFiles = summary.CanceledFiles,
                        SuccessRate = summary.SuccessRate,
                        AverageConfidence = summary.AverageConfidence,
                        AverageDuration = summary.AverageDuration,
                        TotalDuration = summary.TotalDuration,
                        ProcessStatus = summary.ProcessStatus,
                        CreatedAt = DateTime.UtcNow.ToString("O")
                    },
                    transaction: transaction);

                // Insertar BatchRunFile por cada documento
                if (runId > 0)
                {
                    const string insertFileSql = @"
                        INSERT INTO BatchRunFile (
                            RunId, FileName, FullPath, SizeBytes,
                            Estado, InstanceId, CorrelationId, RuntimeStatus,
                            EstadoCalidad, ConfianzaGlobal, MensajeError,
                            FechaInicio, FechaFin, OutputJsonPath
                        ) VALUES (
                            @RunId, @FileName, @FullPath, @SizeBytes,
                            @Estado, @InstanceId, @CorrelationId, @RuntimeStatus,
                            @EstadoCalidad, @ConfianzaGlobal, @MensajeError,
                            @FechaInicio, @FechaFin, @OutputJsonPath
                        )";

                    var fileParams = files
                        .Select(f => new
                        {
                            RunId = runId,
                            FileName = f.FileName,
                            FullPath = f.FullPath,
                            SizeBytes = f.SizeBytes,
                            Estado = f.Estado,
                            InstanceId = f.InstanceId,
                            CorrelationId = f.CorrelationId,
                            RuntimeStatus = f.RuntimeStatus,
                            EstadoCalidad = f.EstadoCalidad,
                            ConfianzaGlobal = (double?)f.ConfianzaGlobal,
                            MensajeError = f.MensajeError,
                            FechaInicio = f.FechaInicio?.ToString("O"),
                            FechaFin = f.FechaFin?.ToString("O"),
                            OutputJsonPath = f.OutputJsonPath
                        })
                        .ToList();

                    await connection.ExecuteAsync(insertFileSql, fileParams, transaction: transaction);
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            // Log pero no interrumpe el flujo principal (best-effort)
            System.Diagnostics.Debug.WriteLine($"[BatchHistorialService] Error guardando run: {ex.Message}");
        }
    }

    /// <summary>
    /// Obtiene el listado de runs filtrado por fecha y tipología.
    /// </summary>
    public async Task<IReadOnlyList<BatchRunRecord>> GetRunsAsync(
        DateTime? from = null,
        DateTime? to = null,
        string? tipologia = null,
        int page = 0,
        int pageSize = 50)
    {
        try
        {
            await using var connection = new SqliteConnection(
                string.Format(ConnectionStringTemplate, _dbPath));
            await connection.OpenAsync();

            var sql = new System.Text.StringBuilder(@"
                SELECT Id, RunKey, RunFolderPath, OperationName, Tipologia,
                       NumeroColas, UmbralConfianza, SubirAGdc, EjecutarConAssetResolver,
                       TotalFiles, CompletedFiles, RevisionFiles, ErrorFiles, CanceledFiles,
                       SuccessRate, AverageConfidence, AverageDuration, TotalDuration,
                       ProcessStatus, CreatedAt
                FROM BatchRun
                WHERE 1=1");

            var parameters = new Dictionary<string, object>();

            if (from.HasValue)
            {
                sql.Append(" AND datetime(CreatedAt) >= datetime(@From)");
                parameters["From"] = from.Value.ToString("O");
            }

            if (to.HasValue)
            {
                sql.Append(" AND datetime(CreatedAt) <= datetime(@To)");
                parameters["To"] = to.Value.AddDays(1).ToString("O"); // Include whole day
            }

            if (!string.IsNullOrEmpty(tipologia) && tipologia != "Todas")
            {
                sql.Append(" AND Tipologia = @Tipologia");
                parameters["Tipologia"] = tipologia;
            }

            sql.Append(" ORDER BY CreatedAt DESC");
            sql.Append($" LIMIT {pageSize} OFFSET {page * pageSize}");

            var runs = await connection.QueryAsync<BatchRunRecord>(
                sql.ToString(),
                parameters.Count > 0 ? parameters : null);

            return runs.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BatchHistorialService] Error en GetRunsAsync: {ex.Message}");
            return new List<BatchRunRecord>();
        }
    }

    /// <summary>
    /// Obtiene todos los archivos de un run específico.
    /// </summary>
    public async Task<IReadOnlyList<BatchRunFileRecord>> GetRunFilesAsync(int runId)
    {
        try
        {
            await using var connection = new SqliteConnection(
                string.Format(ConnectionStringTemplate, _dbPath));
            await connection.OpenAsync();

            const string sql = @"
                SELECT Id, RunId, FileName, FullPath, SizeBytes,
                       Estado, InstanceId, CorrelationId, RuntimeStatus,
                       EstadoCalidad, ConfianzaGlobal, MensajeError,
                       FechaInicio, FechaFin, OutputJsonPath
                FROM BatchRunFile
                WHERE RunId = @RunId
                ORDER BY FileName";

            var files = await connection.QueryAsync<BatchRunFileRecord>(
                sql,
                new { RunId = runId });

            return files.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BatchHistorialService] Error en GetRunFilesAsync: {ex.Message}");
            return new List<BatchRunFileRecord>();
        }
    }

    /// <summary>
    /// Obtiene el total de runs que coinciden con los filtros.
    /// </summary>
    public async Task<int> GetTotalRunsAsync(DateTime? from = null, DateTime? to = null, string? tipologia = null)
    {
        try
        {
            await using var connection = new SqliteConnection(
                string.Format(ConnectionStringTemplate, _dbPath));
            await connection.OpenAsync();

            var sql = new System.Text.StringBuilder("SELECT COUNT(*) FROM BatchRun WHERE 1=1");
            var parameters = new Dictionary<string, object>();

            if (from.HasValue)
            {
                sql.Append(" AND datetime(CreatedAt) >= datetime(@From)");
                parameters["From"] = from.Value.ToString("O");
            }

            if (to.HasValue)
            {
                sql.Append(" AND datetime(CreatedAt) <= datetime(@To)");
                parameters["To"] = to.Value.AddDays(1).ToString("O");
            }

            if (!string.IsNullOrEmpty(tipologia) && tipologia != "Todas")
            {
                sql.Append(" AND Tipologia = @Tipologia");
                parameters["Tipologia"] = tipologia;
            }

            var count = await connection.ExecuteScalarAsync<int>(
                sql.ToString(),
                parameters.Count > 0 ? parameters : null);

            return count;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BatchHistorialService] Error en GetTotalRunsAsync: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Obtiene la lista de tipologías únicas registradas en el historial.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableTipologiasAsync()
    {
        try
        {
            await using var connection = new SqliteConnection(
                string.Format(ConnectionStringTemplate, _dbPath));
            await connection.OpenAsync();

            const string sql = "SELECT DISTINCT Tipologia FROM BatchRun ORDER BY Tipologia";

            var tipologias = await connection.QueryAsync<string>(sql);
            return tipologias.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BatchHistorialService] Error en GetAvailableTipologiasAsync: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Elimina un run y todos sus archivos de la base de datos.
    /// </summary>
    public async Task DeleteRunAsync(int runId)
    {
        await using var connection = new SqliteConnection(
            string.Format(ConnectionStringTemplate, _dbPath));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await connection.ExecuteAsync(
                "DELETE FROM BatchRunFile WHERE RunId = @RunId",
                new { RunId = runId },
                transaction: transaction);

            await connection.ExecuteAsync(
                "DELETE FROM BatchRun WHERE Id = @Id",
                new { Id = runId },
                transaction: transaction);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // -------------------------------------------------------------------------
    // Importación de ejecuciones previas desde carpetas runs/
    // -------------------------------------------------------------------------

    /// <summary>
    /// Escanea la carpeta runs/ y registra en historial las ejecuciones que aún no existan.
    /// Devuelve (importadas, omitidas) donde omitidas = ya existían en BD.
    /// </summary>
    public async Task<(int Imported, int Skipped)> ImportExistingRunsAsync(
        IProgress<string>? progress = null)
    {
        var runsRoot = Path.Combine(AppContext.BaseDirectory, "runs");
        if (!Directory.Exists(runsRoot))
        {
            return (0, 0);
        }

        var extractor = new BatchOutputAuditExtractor();
        var imported = 0;
        var skipped = 0;

        var runFolders = Directory.GetDirectories(runsRoot)
            .Where(d => IsRunKey(Path.GetFileName(d)))
            .OrderBy(d => d)
            .ToList();

        foreach (var folder in runFolders)
        {
            var runKey = Path.GetFileName(folder);

            // Comprobar si ya existe en BD
            if (await RunKeyExistsAsync(runKey))
            {
                skipped++;
                continue;
            }

            progress?.Report($"Importando {runKey}...");

            var jsonFiles = Directory.GetFiles(folder, "*.json");
            if (jsonFiles.Length == 0)
            {
                skipped++;
                continue;
            }

            // Reconstruir archivos desde los JSON de output
            var fileRecords = new List<(string fileName, string outputJsonPath, BatchOutputAuditColumns audit)>();
            foreach (var jsonPath in jsonFiles)
            {
                var audit = extractor.Extract(jsonPath);
                var rawName = Path.GetFileNameWithoutExtension(jsonPath);
                // Formato: {safeName}_{instanceIdOrCorrelationId}
                // Extraemos el nombre quitando el último segmento separado por '_'
                var underscoreIndex = rawName.LastIndexOf('_');
                var displayName = underscoreIndex > 0 ? rawName[..underscoreIndex] : rawName;
                fileRecords.Add((displayName, jsonPath, audit));
            }

            // Calcular resumen sintético
            int completed = 0, revision = 0, error = 0;
            foreach (var (_, _, audit) in fileRecords)
            {
                var estado = InferEstado(audit);
                if (estado == "Completado") completed++;
                else if (estado == "Revisión") revision++;
                else error++;
            }

            var total = fileRecords.Count;
            var tipologiaInferida = fileRecords
                .Select(x => x.audit.IdentificacionTipologiaDetectada)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .GroupBy(t => t)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "Desconocida";

            double? avgConf = null;
            var confValues = fileRecords
                .Select(x => x.audit.ResultadoConfianzaGlobal)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => double.TryParse(
                    v.Replace(",", "."),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d) ? (double?)d : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (confValues.Count > 0)
                avgConf = confValues.Average();

            var successRate = total > 0 ? $"{(double)(completed + revision) / total:P1}" : "0%";
            var avgConfStr = avgConf.HasValue ? $"{avgConf.Value:P1}" : string.Empty;

            // Fecha desde nombre de carpeta
            var createdAt = ParseRunKeyDate(runKey);

            // Insertar en BD
            await ImportRunAsync(
                runKey: runKey,
                runFolderPath: folder,
                tipologia: tipologiaInferida,
                total: total,
                completed: completed,
                revision: revision,
                error: error,
                successRate: successRate,
                avgConfidence: avgConfStr,
                createdAt: createdAt,
                fileRecords: fileRecords);

            imported++;
        }

        return (imported, skipped);
    }

    private async Task<bool> RunKeyExistsAsync(string runKey)
    {
        await using var connection = new SqliteConnection(
            string.Format(ConnectionStringTemplate, _dbPath));
        await connection.OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM BatchRun WHERE RunKey = @RunKey",
            new { RunKey = runKey });
        return count > 0;
    }

    private async Task ImportRunAsync(
        string runKey,
        string runFolderPath,
        string tipologia,
        int total, int completed, int revision, int error,
        string successRate, string avgConfidence,
        DateTime createdAt,
        List<(string fileName, string outputJsonPath, BatchOutputAuditColumns audit)> fileRecords)
    {
        await using var connection = new SqliteConnection(
            string.Format(ConnectionStringTemplate, _dbPath));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            const string insertRunSql = @"
                INSERT INTO BatchRun (
                    RunKey, RunFolderPath, OperationName, Tipologia,
                    NumeroColas, UmbralConfianza, SubirAGdc, EjecutarConAssetResolver,
                    TotalFiles, CompletedFiles, RevisionFiles, ErrorFiles, CanceledFiles,
                    SuccessRate, AverageConfidence, AverageDuration, TotalDuration,
                    ProcessStatus, CreatedAt
                ) VALUES (
                    @RunKey, @RunFolderPath, @OperationName, @Tipologia,
                    @NumeroColas, @UmbralConfianza, @SubirAGdc, @EjecutarConAssetResolver,
                    @TotalFiles, @CompletedFiles, @RevisionFiles, @ErrorFiles, @CanceledFiles,
                    @SuccessRate, @AverageConfidence, @AverageDuration, @TotalDuration,
                    @ProcessStatus, @CreatedAt
                )";

            var runId = await connection.ExecuteScalarAsync<int>(
                insertRunSql,
                new
                {
                    RunKey = runKey,
                    RunFolderPath = runFolderPath,
                    OperationName = "Importado",
                    Tipologia = tipologia,
                    NumeroColas = 0,
                    UmbralConfianza = 0,
                    SubirAGdc = 0,
                    EjecutarConAssetResolver = 0,
                    TotalFiles = total,
                    CompletedFiles = completed,
                    RevisionFiles = revision,
                    ErrorFiles = error,
                    CanceledFiles = 0,
                    SuccessRate = successRate,
                    AverageConfidence = avgConfidence,
                    AverageDuration = string.Empty,
                    TotalDuration = string.Empty,
                    ProcessStatus = "Importado",
                    CreatedAt = createdAt.ToString("O")
                },
                transaction: transaction);

            if (runId > 0)
            {
                const string insertFileSql = @"
                    INSERT INTO BatchRunFile (
                        RunId, FileName, FullPath, SizeBytes,
                        Estado, InstanceId, CorrelationId, RuntimeStatus,
                        EstadoCalidad, ConfianzaGlobal, MensajeError,
                        FechaInicio, FechaFin, OutputJsonPath
                    ) VALUES (
                        @RunId, @FileName, @FullPath, @SizeBytes,
                        @Estado, @InstanceId, @CorrelationId, @RuntimeStatus,
                        @EstadoCalidad, @ConfianzaGlobal, @MensajeError,
                        @FechaInicio, @FechaFin, @OutputJsonPath
                    )";

                var rawName = string.Empty;
                var fileParams = fileRecords.Select(f =>
                {
                    rawName = Path.GetFileNameWithoutExtension(f.outputJsonPath);
                    var underscoreIdx = rawName.LastIndexOf('_');
                    var instanceId = underscoreIdx > 0 ? rawName[(underscoreIdx + 1)..] : string.Empty;

                    var confStr = f.audit.ResultadoConfianzaGlobal;
                    double? conf = double.TryParse(
                        confStr.Replace(",", "."),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var d) ? d : null;

                    return new
                    {
                        RunId = runId,
                        FileName = f.fileName,
                        FullPath = f.outputJsonPath,
                        SizeBytes = 0L,
                        Estado = InferEstado(f.audit),
                        InstanceId = instanceId,
                        CorrelationId = string.Empty,
                        RuntimeStatus = "Completed",
                        EstadoCalidad = f.audit.ResultadoEstadoCalidad,
                        ConfianzaGlobal = conf,
                        MensajeError = f.audit.ResultadoMotivoRevision,
                        FechaInicio = (string?)null,
                        FechaFin = (string?)null,
                        OutputJsonPath = f.outputJsonPath
                    };
                }).ToList();

                await connection.ExecuteAsync(insertFileSql, fileParams, transaction: transaction);
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static string InferEstado(BatchOutputAuditColumns audit)
    {
        if (!string.IsNullOrWhiteSpace(audit.ParseError))
            return "Error";

        var estadoCalidad = audit.ResultadoEstadoCalidad.ToLowerInvariant();
        if (estadoCalidad.Contains("revision") || estadoCalidad.Contains("revisión") ||
            audit.RevisionRequerida.Equals("true", StringComparison.OrdinalIgnoreCase))
            return "Revisión";

        if (estadoCalidad.Contains("error") || estadoCalidad.Contains("fail"))
            return "Error";

        if (!string.IsNullOrWhiteSpace(estadoCalidad))
            return "Completado";

        return "Desconocido";
    }

    private static bool IsRunKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length != 15)
            return false;
        // Pattern: yyyyMMdd-HHmmss
        return name[8] == '-'
            && name[..8].All(char.IsDigit)
            && name[9..].All(char.IsDigit);
    }

    private static DateTime ParseRunKeyDate(string runKey)
    {
        // yyyyMMdd-HHmmss
        if (DateTime.TryParseExact(
                runKey,
                "yyyyMMdd-HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dt))
        {
            return dt;
        }

        return DateTime.UtcNow;
    }
}
