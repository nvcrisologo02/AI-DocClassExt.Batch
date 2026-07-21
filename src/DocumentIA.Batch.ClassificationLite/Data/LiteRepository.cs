using System.IO;
using Dapper;
using DocumentIA.Batch.ClassificationLite.Models;
using Microsoft.Data.Sqlite;

namespace DocumentIA.Batch.ClassificationLite.Data;

public class LiteRepository
{
    private readonly string _connectionString;

    public LiteRepository(string dbPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        EnsureSchema();
    }

    public static string GetDefaultDbPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DocumentIA.BatchLite",
            "lite.db");

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        connection.Execute("PRAGMA journal_mode=WAL;");
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Executions (
                ExecutionId TEXT PRIMARY KEY,
                RootPath TEXT NOT NULL,
                IncludeSubfolders INTEGER NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                ConfigSnapshotJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Documents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExecutionId TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FullPath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                LastModifiedUtc TEXT NOT NULL,
                Status TEXT NOT NULL,
                BatchNumber INTEGER NOT NULL DEFAULT 0,
                RetryCount INTEGER NOT NULL DEFAULT 0,
                InstanceId TEXT NULL,
                StatusQueryUri TEXT NULL,
                Tdn1 TEXT NULL,
                Tdn2 TEXT NULL,
                Confidence REAL NULL,
                Pages INTEGER NULL,
                PagesIncluded TEXT NULL,
                ProcessDate TEXT NULL,
                DurationMs INTEGER NULL,
                RequestJson TEXT NULL,
                ResponseJson TEXT NULL,
                ErrorMessage TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Documents_Dedup
                ON Documents(FileName, FileSize, LastModifiedUtc);
            CREATE INDEX IF NOT EXISTS IX_Documents_Execution
                ON Documents(ExecutionId, Status);
            """);
        connection.Execute("PRAGMA user_version=1;");
    }

    public LiteExecution CreateExecution(
        string rootPath,
        bool includeSubfolders,
        string configSnapshotJson,
        string status = LiteExecutionStatus.Running)
    {
        var execution = new LiteExecution
        {
            ExecutionId = Guid.NewGuid().ToString(),
            RootPath = rootPath,
            IncludeSubfolders = includeSubfolders,
            Status = status,
            StartedAt = DateTime.UtcNow.ToString("O"),
            ConfigSnapshotJson = configSnapshotJson
        };

        using var connection = Open();
        connection.Execute("""
            INSERT INTO Executions (ExecutionId, RootPath, IncludeSubfolders, Status, StartedAt, CompletedAt, ConfigSnapshotJson)
            VALUES (@ExecutionId, @RootPath, @IncludeSubfolders, @Status, @StartedAt, NULL, @ConfigSnapshotJson);
            """, execution);
        return execution;
    }

    public void UpdateExecutionStatus(string executionId, string status, bool setCompletedAt = false)
    {
        using var connection = Open();
        connection.Execute("""
            UPDATE Executions
            SET Status = @status,
                CompletedAt = CASE WHEN @setCompletedAt THEN @now ELSE CompletedAt END
            WHERE ExecutionId = @executionId;
            """, new { executionId, status, setCompletedAt, now = DateTime.UtcNow.ToString("O") });
    }

    public LiteExecution? GetIncompleteExecution()
    {
        using var connection = Open();
        return connection.QueryFirstOrDefault<LiteExecution>("""
            SELECT * FROM Executions
            WHERE Status IN ('Running', 'Paused')
            ORDER BY StartedAt DESC
            LIMIT 1;
            """);
    }

    public void InsertDocuments(IEnumerable<LiteDocument> documents)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            INSERT INTO Documents (
                ExecutionId, FileName, FullPath, FileSize, LastModifiedUtc, Status, BatchNumber, RetryCount,
                InstanceId, StatusQueryUri, Tdn1, Tdn2, Confidence, Pages, PagesIncluded,
                ProcessDate, DurationMs, RequestJson, ResponseJson, ErrorMessage)
            VALUES (
                @ExecutionId, @FileName, @FullPath, @FileSize, @LastModifiedUtc, @Status, @BatchNumber, @RetryCount,
                @InstanceId, @StatusQueryUri, @Tdn1, @Tdn2, @Confidence, @Pages, @PagesIncluded,
                @ProcessDate, @DurationMs, @RequestJson, @ResponseJson, @ErrorMessage);
            """, documents, transaction);
        transaction.Commit();
    }

    public void UpdateDocument(LiteDocument document)
    {
        using var connection = Open();
        connection.Execute("""
            UPDATE Documents SET
                Status = @Status,
                BatchNumber = @BatchNumber,
                RetryCount = @RetryCount,
                InstanceId = @InstanceId,
                StatusQueryUri = @StatusQueryUri,
                Tdn1 = @Tdn1,
                Tdn2 = @Tdn2,
                Confidence = @Confidence,
                Pages = @Pages,
                PagesIncluded = @PagesIncluded,
                ProcessDate = @ProcessDate,
                DurationMs = @DurationMs,
                RequestJson = @RequestJson,
                ResponseJson = @ResponseJson,
                ErrorMessage = @ErrorMessage
            WHERE Id = @Id;
            """, document);
    }

    public List<LiteDocument> GetDocuments(string executionId)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>(
            "SELECT * FROM Documents WHERE ExecutionId = @executionId ORDER BY Id;",
            new { executionId }).ToList();
    }

    /// <summary>
    /// Lectura ligera para la rejilla: excluye RequestJson y ResponseJson (varios KB por
    /// documento). Pensada para el refresco periodico de la ventana, que puede recorrer hasta
    /// 100.000 filas por ejecucion: cargar los JSON completos en cada refresco bloquearia el
    /// hilo de interfaz. El detalle bajo demanda usa <see cref="GetDocument"/>.
    /// </summary>
    public List<LiteDocument> GetDocumentsForGrid(string executionId)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT
                Id, ExecutionId, FileName, FullPath, FileSize, LastModifiedUtc, Status, BatchNumber,
                RetryCount, InstanceId, StatusQueryUri, Tdn1, Tdn2, Confidence, Pages, PagesIncluded,
                ProcessDate, DurationMs, ErrorMessage
            FROM Documents
            WHERE ExecutionId = @executionId
            ORDER BY Id;
            """, new { executionId }).ToList();
    }

    /// <summary>Documento completo por Id, incluyendo RequestJson/ResponseJson, para el dialogo de detalle.</summary>
    public LiteDocument? GetDocument(long id)
    {
        using var connection = Open();
        return connection.QueryFirstOrDefault<LiteDocument>(
            "SELECT * FROM Documents WHERE Id = @id;",
            new { id });
    }

    public LiteDocument? FindLastSucceeded(string fileName, long fileSize, string lastModifiedUtc)
    {
        using var connection = Open();
        return connection.QueryFirstOrDefault<LiteDocument>("""
            SELECT * FROM Documents
            WHERE FileName = @fileName AND FileSize = @fileSize AND LastModifiedUtc = @lastModifiedUtc
              AND Status = 'Succeeded'
            ORDER BY Id DESC
            LIMIT 1;
            """, new { fileName, fileSize, lastModifiedUtc });
    }

    public List<int> GetBatchNumbers(string executionId)
    {
        using var connection = Open();
        return connection.Query<int>("""
            SELECT DISTINCT BatchNumber FROM Documents
            WHERE ExecutionId = @executionId AND Status IN ('Pending', 'Error', 'InFlight')
            ORDER BY BatchNumber;
            """, new { executionId }).ToList();
    }

    public List<LiteDocument> GetPendingBatch(string executionId, int batchNumber)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT * FROM Documents
            WHERE ExecutionId = @executionId AND BatchNumber = @batchNumber AND Status = 'Pending'
            ORDER BY Id;
            """, new { executionId, batchNumber }).ToList();
    }

    public List<LiteDocument> GetErrorsInBatch(string executionId, int batchNumber)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT * FROM Documents
            WHERE ExecutionId = @executionId AND BatchNumber = @batchNumber AND Status = 'Error'
            ORDER BY Id;
            """, new { executionId, batchNumber }).ToList();
    }

    public List<LiteDocument> GetInFlight(string executionId)
    {
        using var connection = Open();
        return connection.Query<LiteDocument>("""
            SELECT * FROM Documents
            WHERE ExecutionId = @executionId AND Status = 'InFlight'
            ORDER BY Id;
            """, new { executionId }).ToList();
    }

    public LiteCounters GetCounters(string executionId)
    {
        using var connection = Open();
        return connection.QuerySingle<LiteCounters>("""
            SELECT
                COUNT(*) AS Total,
                COALESCE(SUM(CASE WHEN Status = 'Pending' THEN 1 ELSE 0 END), 0) AS Pending,
                COALESCE(SUM(CASE WHEN Status = 'InFlight' THEN 1 ELSE 0 END), 0) AS InFlight,
                COALESCE(SUM(CASE WHEN Status = 'Succeeded' THEN 1 ELSE 0 END), 0) AS Succeeded,
                COALESCE(SUM(CASE WHEN Status = 'Error' THEN 1 ELSE 0 END), 0) AS Error,
                COALESCE(SUM(CASE WHEN Status = 'DefinitiveError' THEN 1 ELSE 0 END), 0) AS DefinitiveError,
                COALESCE(SUM(CASE WHEN Status = 'SkippedHistory' THEN 1 ELSE 0 END), 0) AS SkippedHistory,
                COALESCE(SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END), 0) AS Cancelled
            FROM Documents
            WHERE ExecutionId = @executionId;
            """, new { executionId });
    }

    public LiteExecution? GetExecution(string executionId)
    {
        using var connection = Open();
        return connection.QueryFirstOrDefault<LiteExecution>(
            "SELECT * FROM Executions WHERE ExecutionId = @executionId;",
            new { executionId });
    }
}
