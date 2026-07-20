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

    public LiteExecution CreateExecution(string rootPath, bool includeSubfolders, string configSnapshotJson)
    {
        var execution = new LiteExecution
        {
            ExecutionId = Guid.NewGuid().ToString(),
            RootPath = rootPath,
            IncludeSubfolders = includeSubfolders,
            Status = LiteExecutionStatus.Running,
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
}
