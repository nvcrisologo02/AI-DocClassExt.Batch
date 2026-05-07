using DocumentIA.Batch.Models;
using System.Collections.Generic;
using System.Linq;

namespace DocumentIA.Batch.Services;

/// <summary>
/// Servicio para exportar el historial reutilizando exactamente
/// el mismo motor de exportación del Main.
/// </summary>
public class HistorialExportService
{
    private readonly BatchCsvExportService _csvExportService;
    private readonly BatchExcelExportService _excelExportService;

    public HistorialExportService()
        : this(new BatchCsvExportService(), new BatchExcelExportService())
    {
    }

    public HistorialExportService(BatchCsvExportService csvExportService, BatchExcelExportService excelExportService)
    {
        _csvExportService = csvExportService;
        _excelExportService = excelExportService;
    }

    /// <summary>
    /// Exporta una lista de HistorialFileRow directamente a CSV.
    /// </summary>
    public void ExportCsv(string filePath, IEnumerable<HistorialFileRow> fileRows)
    {
        var files = fileRows.Select(MapToBatchFileItem).ToList();
        var tipologia = fileRows.Select(r => r.Tipologia).FirstOrDefault() ?? string.Empty;

        _csvExportService.Export(
            filePath,
            files,
            tipologia,
            numeroColas: 0,
            umbralConfianza: 0,
            subirAGdc: false,
            ejecutarConAssetResolver: false);
    }

    /// <summary>
    /// Exporta una lista de HistorialFileRow a Excel.
    /// </summary>
    public void ExportExcel(string filePath, IEnumerable<HistorialFileRow> fileRows)
    {
        var files = fileRows.Select(MapToBatchFileItem).ToList();
        var tipologia = fileRows.Select(r => r.Tipologia).FirstOrDefault() ?? string.Empty;

        _excelExportService.Export(
            filePath,
            files,
            tipologia,
            numeroColas: 0,
            umbralConfianza: 0,
            subirAGdc: false,
            ejecutarConAssetResolver: false);
    }

    /// <summary>
    /// Exporta los archivos de un run a CSV.
    /// </summary>
    public void ExportCsv(string filePath, BatchRunRecord run, IEnumerable<BatchRunFileRecord> files)
    {
        var mapped = files.Select(MapToBatchFileItem).ToList();
        _csvExportService.Export(
            filePath,
            mapped,
            run.Tipologia,
            numeroColas: run.NumeroColas,
            umbralConfianza: run.UmbralConfianza,
            subirAGdc: run.SubirAGdc,
            ejecutarConAssetResolver: run.EjecutarConAssetResolver);
    }

    public void ExportCsv(string filePath, IEnumerable<HistorialExportRow> rows)
    {
        var list = rows.ToList();
        var mapped = list.Select(MapToBatchFileItem).ToList();
        var firstRun = list.Select(r => r.Run).FirstOrDefault();

        _csvExportService.Export(
            filePath,
            mapped,
            firstRun?.Tipologia ?? string.Empty,
            numeroColas: firstRun?.NumeroColas ?? 0,
            umbralConfianza: firstRun?.UmbralConfianza ?? 0,
            subirAGdc: firstRun?.SubirAGdc ?? false,
            ejecutarConAssetResolver: firstRun?.EjecutarConAssetResolver ?? false);
    }

    /// <summary>
    /// Exporta los archivos de un run a Excel (.xlsx).
    /// Crea un XLSX válido sin Office ni ClosedXML usando ZipArchive + XmlWriter.
    /// </summary>
    public void ExportExcel(string filePath, BatchRunRecord run, IEnumerable<BatchRunFileRecord> files)
    {
        var mapped = files.Select(MapToBatchFileItem).ToList();
        _excelExportService.Export(
            filePath,
            mapped,
            run.Tipologia,
            numeroColas: run.NumeroColas,
            umbralConfianza: run.UmbralConfianza,
            subirAGdc: run.SubirAGdc,
            ejecutarConAssetResolver: run.EjecutarConAssetResolver);
    }

    public void ExportExcel(string filePath, IEnumerable<HistorialExportRow> rows)
    {
        var list = rows.ToList();
        var mapped = list.Select(MapToBatchFileItem).ToList();
        var firstRun = list.Select(r => r.Run).FirstOrDefault();

        _excelExportService.Export(
            filePath,
            mapped,
            firstRun?.Tipologia ?? string.Empty,
            numeroColas: firstRun?.NumeroColas ?? 0,
            umbralConfianza: firstRun?.UmbralConfianza ?? 0,
            subirAGdc: firstRun?.SubirAGdc ?? false,
            ejecutarConAssetResolver: firstRun?.EjecutarConAssetResolver ?? false);
    }

    private static BatchFileItem MapToBatchFileItem(HistorialFileRow row)
    {
        return new BatchFileItem
        {
            FileName = row.FileName,
            FullPath = row.FullPath,
            SizeBytes = row.SizeBytes,
            Estado = row.Estado,
            InstanceId = row.InstanceId ?? string.Empty,
            CorrelationId = row.CorrelationId ?? string.Empty,
            RuntimeStatus = row.RuntimeStatus ?? string.Empty,
            EstadoCalidad = row.EstadoCalidad ?? string.Empty,
            ConfianzaGlobal = row.ConfianzaGlobal,
            MensajeError = row.MensajeError ?? string.Empty,
            FechaInicio = row.FechaInicio,
            FechaFin = row.FechaFin,
            OutputJsonPath = row.OutputJsonPath ?? string.Empty
        };
    }

    private static BatchFileItem MapToBatchFileItem(BatchRunFileRecord file)
    {
        return new BatchFileItem
        {
            FileName = file.FileName,
            FullPath = file.FullPath,
            SizeBytes = file.SizeBytes,
            Estado = file.Estado,
            InstanceId = file.InstanceId ?? string.Empty,
            CorrelationId = file.CorrelationId ?? string.Empty,
            RuntimeStatus = file.RuntimeStatus ?? string.Empty,
            EstadoCalidad = file.EstadoCalidad ?? string.Empty,
            ConfianzaGlobal = file.ConfianzaGlobal,
            MensajeError = file.MensajeError ?? string.Empty,
            FechaInicio = file.FechaInicio,
            FechaFin = file.FechaFin,
            OutputJsonPath = file.OutputJsonPath ?? string.Empty
        };
    }

    private static BatchFileItem MapToBatchFileItem(HistorialExportRow row)
    {
        return MapToBatchFileItem(row.File);
    }
}
