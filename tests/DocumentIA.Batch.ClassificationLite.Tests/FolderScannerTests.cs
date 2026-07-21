using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using DocumentIA.Batch.ClassificationLite.Data;
using DocumentIA.Batch.ClassificationLite.Engine;
using DocumentIA.Batch.ClassificationLite.Models;
using Xunit;

namespace DocumentIA.Batch.ClassificationLite.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _docsDir;
    private readonly LiteRepository _repository;
    private readonly FolderScanner _scanner;

    public FolderScannerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lite-scan-tests-" + Guid.NewGuid().ToString("N"));
        _docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(_docsDir);
        _repository = new LiteRepository(Path.Combine(_tempDir, "lite.db"));
        _scanner = new FolderScanner(_repository);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreatePdf(string relativePath, string content = "pdf")
    {
        var fullPath = Path.Combine(_docsDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Fact]
    public void Scan_EncolaSoloPdfs_YAsignaLotes()
    {
        CreatePdf("a.pdf");
        CreatePdf("b.pdf");
        CreatePdf("c.pdf");
        CreatePdf("ignorame.txt");
        var execution = _repository.CreateExecution(_docsDir, false, "{}");

        var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, includeSubfolders: false,
            skipAlreadyProcessed: true, forceReprocess: false, internalBatchSize: 2, CancellationToken.None);

        Assert.Equal(3, result.TotalFound);
        Assert.Equal(3, result.Enqueued);
        Assert.Equal(0, result.Skipped);
        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.Equal(3, docs.Count);
        Assert.All(docs, d => Assert.Equal(LiteDocumentStatus.Pending, d.Status));
        Assert.Equal(new[] { 1, 1, 2 }, docs.OrderBy(d => d.Id).Select(d => d.BatchNumber).ToArray());
        Assert.All(docs, d => Assert.True(d.FileSize > 0));
        Assert.All(docs, d => Assert.NotEqual(string.Empty, d.LastModifiedUtc));
    }

    [Fact]
    public void Scan_SinSubcarpetas_IgnoraAnidados_ConSubcarpetas_LosIncluye()
    {
        CreatePdf("raiz.pdf");
        CreatePdf(Path.Combine("sub", "anidado.pdf"));

        var e1 = _repository.CreateExecution(_docsDir, false, "{}");
        var r1 = _scanner.Scan(e1.ExecutionId, new[] { _docsDir }, false, true, false, 1000, CancellationToken.None);
        Assert.Equal(1, r1.TotalFound);

        var e2 = _repository.CreateExecution(_docsDir, true, "{}");
        var r2 = _scanner.Scan(e2.ExecutionId, new[] { _docsDir }, true, true, false, 1000, CancellationToken.None);
        Assert.Equal(2, r2.TotalFound);
    }

    [Fact]
    public void Scan_DocumentoYaProcesado_SeOmiteConDatosHistoricos()
    {
        var path = CreatePdf("repetido.pdf");
        var info = new FileInfo(path);
        var previous = _repository.CreateExecution(@"c:\otra", false, "{}");
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = previous.ExecutionId,
                FileName = "repetido.pdf",
                FullPath = @"c:\otra\repetido.pdf",
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Succeeded,
                Tdn1 = "T09",
                Tdn2 = "T09.01",
                Confidence = 0.88,
                Pages = 3,
                PagesIncluded = "1-3",
                ProcessDate = "2026-07-19T09:00:00Z",
                DurationMs = 1500
            }
        });

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, false, true, false, 1000, CancellationToken.None);

        Assert.Equal(1, result.TotalFound);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Enqueued);
        var doc = _repository.GetDocuments(execution.ExecutionId).Single();
        Assert.Equal(LiteDocumentStatus.SkippedHistory, doc.Status);
        Assert.Equal("T09", doc.Tdn1);
        Assert.Equal("T09.01", doc.Tdn2);
        Assert.Equal(0.88, doc.Confidence);
        Assert.Equal("1-3", doc.PagesIncluded);
    }

    [Fact]
    public void Scan_ConForceReprocess_NoOmiteNada()
    {
        var path = CreatePdf("repetido.pdf");
        var info = new FileInfo(path);
        var previous = _repository.CreateExecution(@"c:\otra", false, "{}");
        _repository.InsertDocuments(new[]
        {
            new LiteDocument
            {
                ExecutionId = previous.ExecutionId,
                FileName = "repetido.pdf",
                FullPath = @"c:\otra\repetido.pdf",
                FileSize = info.Length,
                LastModifiedUtc = info.LastWriteTimeUtc.ToString("O"),
                Status = LiteDocumentStatus.Succeeded
            }
        });

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, false, true, true, 1000, CancellationToken.None);

        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Enqueued);
        Assert.Equal(LiteDocumentStatus.Pending, _repository.GetDocuments(execution.ExecutionId).Single().Status);
    }

    [Fact]
    public void Scan_FicheroSuelto_TambienSeEncola()
    {
        var path = CreatePdf("suelto.pdf");
        var execution = _repository.CreateExecution(path, false, "{}");

        var result = _scanner.Scan(execution.ExecutionId, new[] { path }, false, true, false, 1000, CancellationToken.None);

        Assert.Equal(1, result.Enqueued);
    }

    // Nota de cobertura: el backstop real del defecto Critical (el try/catch alrededor del
    // avance del enumerador, EnumeratePdfFiles lineas 172-190 en FolderScanner.cs) protege
    // contra un IOException/UnauthorizedAccessException lanzado por MoveNext() DESPUES de
    // que Directory.EnumerateFiles ya ha empezado a iterar (p.ej. una carpeta de red que se
    // desconecta a mitad de enumeracion). El escenario de ACL denegada de abajo, en cambio,
    // queda cubierto por EnumerationOptions.IgnoreInaccessible=true, que evita que la
    // excepcion llegue siquiera a nuestro propio try/catch. Directory.EnumerateFiles se
    // invoca directamente (no hay una seam de inyeccion), asi que no es viable, sin tocar
    // el codigo de produccion, forzar un enumerador que lance en MoveNext(); por eso ese
    // camino queda sin test dedicado.
    [Fact]
    public void Scan_SubcarpetaInaccesible_NoAbortaElEscaneo()
    {
        CreatePdf("raiz1.pdf");
        CreatePdf("raiz2.pdf");
        var subDir = Path.Combine(_docsDir, "restringida");
        Directory.CreateDirectory(subDir);
        CreatePdf(Path.Combine("restringida", "oculto.pdf"));

        var aclApplied = false;
        FileSystemAccessRule? denyRule = null;
        var identity = WindowsIdentity.GetCurrent().User;

        if (identity is not null)
        {
            var directoryInfo = new DirectoryInfo(subDir);
            try
            {
                var security = directoryInfo.GetAccessControl();
                denyRule = new FileSystemAccessRule(
                    identity,
                    FileSystemRights.ListDirectory,
                    AccessControlType.Deny);
                security.AddAccessRule(denyRule);
                directoryInfo.SetAccessControl(security);
                aclApplied = true;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or SystemException)
            {
                // Sin privilegios para aplicar ACLs en este entorno: se ejecuta igualmente
                // la parte valida del test (raiz accesible), sin subcarpeta restringida.
            }
        }

        try
        {
            var execution = _repository.CreateExecution(_docsDir, true, "{}");

            var result = _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, includeSubfolders: true,
                skipAlreadyProcessed: true, forceReprocess: false, internalBatchSize: 1000, CancellationToken.None);

            Assert.True(result.TotalFound >= 2);
            var docs = _repository.GetDocuments(execution.ExecutionId);
            Assert.Contains(docs, d => d.FileName == "raiz1.pdf");
            Assert.Contains(docs, d => d.FileName == "raiz2.pdf");
        }
        finally
        {
            if (aclApplied && denyRule is not null)
            {
                try
                {
                    var cleanupInfo = new DirectoryInfo(subDir);
                    var cleanupSecurity = cleanupInfo.GetAccessControl();
                    cleanupSecurity.RemoveAccessRule(denyRule);
                    cleanupInfo.SetAccessControl(cleanupSecurity);
                }
                catch { }
            }
        }
    }

    [Fact]
    public void Scan_CancelacionAMitad_NoPierdeDocumentosYaBufferizados()
    {
        // 600 PDFs garantizan que el primer bloque de InsertChunkSize (500) se inserte y
        // dispare Progress ANTES de terminar el escaneo (quedan 100 en el segundo bloque).
        // Cancelamos desde ese handler para que la cancelacion ocurra DESPUES de haber
        // bufferizado e insertado documentos (a diferencia del test original, que cancelaba
        // el token antes de procesar ningun fichero y pasaba aunque se quitara el flush).
        // Nota: al cancelar justo cuando Progress se dispara, el buffer ya esta vacio (se
        // limpia antes de invocar el evento), por lo que esta prueba concreta ejercita el
        // flush intermedio dentro del try, no el flush de cola en el finally; ese ultimo
        // camino (buffer parcial < 500 pendiente de insertar) ya esta cubierto por el resto
        // de tests de este fichero, que terminan con menos de 500 ficheros sin cancelacion.
        for (var i = 0; i < 600; i++)
        {
            CreatePdf($"doc{i:D4}.pdf");
        }

        var execution = _repository.CreateExecution(_docsDir, false, "{}");
        using var cts = new CancellationTokenSource();
        var progressCalls = 0;
        _scanner.Progress += _ =>
        {
            progressCalls++;
            if (progressCalls == 1)
            {
                cts.Cancel();
            }
        };

        Assert.Throws<OperationCanceledException>(() =>
            _scanner.Scan(execution.ExecutionId, new[] { _docsDir }, includeSubfolders: false,
                skipAlreadyProcessed: true, forceReprocess: false, internalBatchSize: 1000, cts.Token));

        Assert.Equal(1, progressCalls);
        var docs = _repository.GetDocuments(execution.ExecutionId);
        Assert.Equal(500, docs.Count);
    }
}
