using System.IO;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Models;

namespace DocumentIA.Batch.ClassificationLite.Engine;

public class ScanResult
{
    public int TotalFound { get; set; }
    public int Enqueued { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}

public class FolderScanner
{
    private const int InsertChunkSize = 500;

    private readonly LiteRepository _repository;

    public FolderScanner(LiteRepository repository)
    {
        _repository = repository;
    }

    public event Action<ScanResult>? Progress;

    public ScanResult Scan(
        string executionId,
        IReadOnlyList<string> paths,
        bool includeSubfolders,
        bool skipAlreadyProcessed,
        bool forceReprocess,
        int internalBatchSize,
        CancellationToken cancellationToken)
    {
        var result = new ScanResult();
        var buffer = new List<LiteDocument>(InsertChunkSize);
        var enqueued = 0;

        foreach (var fullPath in EnumeratePdfFiles(paths, includeSubfolders))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.TotalFound++;

            LiteDocument document;
            try
            {
                var info = new FileInfo(fullPath);
                var fileSize = info.Length;
                var lastModifiedUtc = info.LastWriteTimeUtc.ToString("O");

                var historical = skipAlreadyProcessed && !forceReprocess
                    ? _repository.FindLastSucceeded(info.Name, fileSize, lastModifiedUtc)
                    : null;

                if (historical is not null)
                {
                    result.Skipped++;
                    document = new LiteDocument
                    {
                        ExecutionId = executionId,
                        FileName = info.Name,
                        FullPath = fullPath,
                        FileSize = fileSize,
                        LastModifiedUtc = lastModifiedUtc,
                        Status = LiteDocumentStatus.SkippedHistory,
                        BatchNumber = 0,
                        Tdn1 = historical.Tdn1,
                        Tdn2 = historical.Tdn2,
                        Confidence = historical.Confidence,
                        Pages = historical.Pages,
                        PagesIncluded = historical.PagesIncluded,
                        ProcessDate = historical.ProcessDate,
                        DurationMs = historical.DurationMs
                    };
                }
                else
                {
                    document = new LiteDocument
                    {
                        ExecutionId = executionId,
                        FileName = info.Name,
                        FullPath = fullPath,
                        FileSize = fileSize,
                        LastModifiedUtc = lastModifiedUtc,
                        Status = LiteDocumentStatus.Pending,
                        BatchNumber = (enqueued / internalBatchSize) + 1
                    };
                    enqueued++;
                    result.Enqueued++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Failed++;
                document = new LiteDocument
                {
                    ExecutionId = executionId,
                    FileName = Path.GetFileName(fullPath),
                    FullPath = fullPath,
                    FileSize = 0,
                    LastModifiedUtc = string.Empty,
                    Status = LiteDocumentStatus.Error,
                    ErrorMessage = "No se pudo leer el fichero: " + ex.Message,
                    BatchNumber = (enqueued / internalBatchSize) + 1
                };
                enqueued++;
            }

            buffer.Add(document);
            if (buffer.Count >= InsertChunkSize)
            {
                _repository.InsertDocuments(buffer);
                buffer.Clear();
                Progress?.Invoke(result);
            }
        }

        if (buffer.Count > 0)
        {
            _repository.InsertDocuments(buffer);
            Progress?.Invoke(result);
        }

        return result;
    }

    private static IEnumerable<string> EnumeratePdfFiles(IReadOnlyList<string> paths, bool includeSubfolders)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    yield return path;
                }

                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    path,
                    "*.pdf",
                    includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }
}
