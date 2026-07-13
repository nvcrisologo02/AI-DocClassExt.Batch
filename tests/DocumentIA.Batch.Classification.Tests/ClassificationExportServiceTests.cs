using System.IO;
using System.IO.Compression;
using System.Text;
using DocumentIA.Batch.Classification.Models;
using DocumentIA.Batch.Classification.Services;
using Xunit;

namespace DocumentIA.Batch.Classification.Tests;

public class ClassificationExportServiceTests
{
    [Fact]
    public void ExportCsv_WritesCurrentContractHeadersAndValues()
    {
        var service = new ClassificationExportService();
        var path = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.csv");

        try
        {
            service.ExportCsv(path, new[]
            {
                new ClassificationDocumentItem
                {
                    FileName = "doc1.pdf",
                    Status = "OK",
                    TipologiaIdentificada = "nota.simple.1_4",
                    ConfianzaGlobal = "0.97",
                    Clasificador = "RuleBasedTDN",
                    FallbackLlm = "false",
                    DuracionTotalMs = "1234"
                }
            });

            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);

            var text = File.ReadAllText(path, Encoding.UTF8);
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(lines.Length >= 2);

            var header = lines[0];
            Assert.Contains("FileName;Status;Typology;TipologiaFamilia;TipologiaVersion", header);
            Assert.Contains("Clasificador;Confidence;FallbackLLM;DuracionTotalMs", header);
            Assert.Contains("DetalleProveedores.Reglas.Tipologia", header);
            Assert.Contains("ReutilizadaPorDuplicado", header);

            var row = lines[1].Split(';');
            Assert.Equal("doc1.pdf", row[0]);
            Assert.Equal("OK", row[1]);
            Assert.Equal("nota.simple.1_4", row[2]);
            Assert.Equal("RuleBasedTDN", row[11]);
            Assert.Equal("97,0 % (raw: 0.97)", row[12]);
            Assert.Equal("false", row[13]);
            Assert.Equal("1234", row[14]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ExportExcel_WritesValidZipWorkbook()
    {
        var service = new ClassificationExportService();
        var path = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.xlsx");

        try
        {
            service.ExportExcel(path, new[]
            {
                new ClassificationDocumentItem
                {
                    FileName = "doc1.pdf",
                    IdentificacionDocumento = "ID-001",
                    TipologiaIdentificada = "nota.simple.1_4",
                    ConfianzaGlobal = "0.97"
                }
            });

            using var archive = ZipFile.OpenRead(path);
            Assert.Contains(archive.Entries, entry => entry.FullName == "xl/workbook.xml");
            Assert.Contains(archive.Entries, entry => entry.FullName == "xl/worksheets/sheet1.xml");

            using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            using var reader = new StreamReader(sheetStream, Encoding.UTF8);
            var worksheetXml = reader.ReadToEnd();

            Assert.Contains("FileName", worksheetXml);
            Assert.Contains("Status", worksheetXml);
            Assert.Contains("Confidence", worksheetXml);
            Assert.Contains("doc1.pdf", worksheetXml);
            Assert.Contains("0.97", worksheetXml);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ExportSimplifiedExcel_WritesRequestedHeadersAndValues()
    {
        var service = new ClassificationExportService();
        var path = Path.Combine(Path.GetTempPath(), $"classification-simplified-{Guid.NewGuid():N}.xlsx");

        try
        {
            service.ExportSimplifiedExcel(path, new[]
            {
                new ClassificationDocumentItem
                {
                    FileName = "doc-simple.pdf",
                    Status = "Completado",
                    ResultadoEstado = "OK",
                    TipologiaIdentificada = "nota.simple.1_4",
                    PaginasIncluidas = "1-2",
                    Paginas = "4",
                    Tdn1 = "NS",
                    Tdn2 = "NS-14",
                    Matricula = "MAT-001",
                    Clasificador = "RuleBasedTDN",
                    ConfianzaGlobal = "0.93",
                    DuracionTotalMs = "987",
                    Resumen = "Documento con nota simple.",
                    TipologiaNombre = "Nota Simple"
                }
            });

            using var archive = ZipFile.OpenRead(path);
            using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            using var reader = new StreamReader(sheetStream, Encoding.UTF8);
            var worksheetXml = reader.ReadToEnd();

            Assert.Contains("Filename", worksheetXml);
            Assert.Contains("Status", worksheetXml);
            Assert.Contains("Rsultado", worksheetXml);
            Assert.Contains("Tipologia", worksheetXml);
            Assert.Contains("PaginasIncluidas", worksheetXml);
            Assert.Contains("Paginas", worksheetXml);
            Assert.Contains("Tdn1", worksheetXml);
            Assert.Contains("Tdn2", worksheetXml);
            Assert.Contains("Matricula", worksheetXml);
            Assert.Contains("Clasificador", worksheetXml);
            Assert.Contains("Confidence", worksheetXml);
            Assert.Contains("DuracionTotalMs", worksheetXml);
            Assert.Contains("Resumen", worksheetXml);
            Assert.Contains("TiplogiaVirtual", worksheetXml);
            Assert.Contains("EsTipologiaVirtual", worksheetXml);

            Assert.Contains("doc-simple.pdf", worksheetXml);
            Assert.Contains("Completado", worksheetXml);
            Assert.Contains("OK", worksheetXml);
            Assert.Contains("nota.simple.1_4", worksheetXml);
            Assert.Contains("1-2", worksheetXml);
            Assert.Contains("4", worksheetXml);
            Assert.Contains("NS", worksheetXml);
            Assert.Contains("NS-14", worksheetXml);
            Assert.Contains("MAT-001", worksheetXml);
            Assert.Contains("RuleBasedTDN", worksheetXml);
            Assert.Contains("93,0 % (raw: 0.93)", worksheetXml);
            Assert.Contains("987", worksheetXml);
            Assert.Contains("Documento con nota simple.", worksheetXml);
            Assert.Contains("Nota Simple", worksheetXml);
            Assert.Contains("false", worksheetXml);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

        [Fact]
        public void ExportSimplifiedExcel_MarksTipologiaVirtualAsPropuesta_WhenClasificacionParcial()
        {
                var service = new ClassificationExportService();
                var excelPath = Path.Combine(Path.GetTempPath(), $"classification-simplified-{Guid.NewGuid():N}.xlsx");
                var jsonPath = Path.Combine(Path.GetTempPath(), $"classification-simplified-{Guid.NewGuid():N}.json");

                try
                {
                        File.WriteAllText(jsonPath, """
                        {
                            "Identificacion": {
                                "TipologiaNombre": "Contrato de arrendamiento",
                                "PropuestaTipologia": "Contrato de arrendamiento"
                            },
                            "Resultado": {
                                "Estado": "REVISION"
                            },
                            "DetalleEjecucion": {
                                "Clasificacion": {
                                    "ClasificacionParcial": true,
                                    "PropuestaTipologia": "Contrato de arrendamiento"
                                }
                            }
                        }
                        """);

                        service.ExportSimplifiedExcel(excelPath, new[]
                        {
                                new ClassificationDocumentItem
                                {
                                        FileName = "doc-propuesta.pdf",
                                        OutputJsonPath = jsonPath
                                }
                        });

                        using var archive = ZipFile.OpenRead(excelPath);
                        using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
                        using var reader = new StreamReader(sheetStream, Encoding.UTF8);
                        var worksheetXml = reader.ReadToEnd();

                        Assert.Contains("Contrato de arrendamiento", worksheetXml);
                        Assert.Contains("true", worksheetXml);
                }
                finally
                {
                        if (File.Exists(excelPath))
                        {
                                File.Delete(excelPath);
                        }

                        if (File.Exists(jsonPath))
                        {
                                File.Delete(jsonPath);
                        }
                }
        }

        [Fact]
        public void ExportCsv_TipologiaVirtual_TomaTdn2DeTdn2Detectado()
        {
                var service = new ClassificationExportService();
                var csvPath = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.csv");
                var jsonPath = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.json");

                try
                {
                        // Resultado virtual TDN1: Identificacion sin Tdn2, pero la clasificación
                        // informa el TDN2 elegido en Phase 2 via Tdn2Detectado.
                        File.WriteAllText(jsonPath, """
                        {
                            "Identificacion": {
                                "Tipologia": "ESIN",
                                "TipologiaFamilia": "ESIN",
                                "Tdn1": "ESIN"
                            },
                            "Resultado": {
                                "Estado": "OK"
                            },
                            "DetalleEjecucion": {
                                "Clasificacion": {
                                    "Confianza": "0.9",
                                    "ClasificacionParcial": true,
                                    "FallbackRazon": "Tipologia Virtual",
                                    "Tdn2Detectado": "ESIN-40"
                                }
                            }
                        }
                        """);

                        service.ExportCsv(csvPath, new[]
                        {
                                new ClassificationDocumentItem
                                {
                                        FileName = "esin-40.pdf",
                                        OutputJsonPath = jsonPath
                                }
                        });

                        var text = File.ReadAllText(csvPath, Encoding.UTF8);
                        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Assert.True(lines.Length >= 2);

                        var row = lines[1].Split(';');
                        Assert.Equal("esin-40.pdf", row[0]);
                        Assert.Equal("ESIN", row[2]);
                        Assert.Equal("ESIN", row[8]);
                        Assert.Equal("ESIN-40", row[9]);
                }
                finally
                {
                        if (File.Exists(csvPath))
                        {
                                File.Delete(csvPath);
                        }

                        if (File.Exists(jsonPath))
                        {
                                File.Delete(jsonPath);
                        }
                }
        }

        [Fact]
        public void ExportCsv_PrefersOutputSnapshotAndIncludesProviderDetails()
        {
                var service = new ClassificationExportService();
                var csvPath = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.csv");
                var jsonPath = Path.Combine(Path.GetTempPath(), $"classification-{Guid.NewGuid():N}.json");

                try
                {
                        File.WriteAllText(jsonPath, """
                        {
                            "Identificacion": {
                                "Tipologia": "escr.10",
                                "TipologiaFamilia": "ESCR",
                                "TipologiaVersion": "1.0",
                                "FechaProceso": "2026-05-27T10:00:00Z",
                                "Paginas": "5",
                                "Tdn1": "ESCR",
                                "Tdn2": "ESCR-10",
                                "Matricula": "M-0001"
                            },
                            "Resultado": {
                                "Estado": "VALIDACION_CON_ERRORES",
                                "ReutilizadaPorDuplicado": true
                            },
                            "DetalleEjecucion": {
                                "PaginasIncluidas": "1-3",
                                "Clasificacion": {
                                    "Clasificador": "FoundryRescue",
                                    "Confianza": "0.62",
                                    "FallbackLLM": "true",
                                    "DetalleProveedores": [
                                        {
                                            "Proveedor": "Reglas",
                                            "Tipologia": "escr.10",
                                            "Confianza": "0.81",
                                            "MotivoDescarte": "none"
                                        },
                                        {
                                            "Proveedor": "DI",
                                            "Tipologia": "escr.11",
                                            "Confianza": "0.44",
                                            "MotivoDescarte": "low-confidence"
                                        }
                                    ]
                                },
                                "Seguimiento": {
                                    "DuracionTotalMs": "4567"
                                }
                            }
                        }
                        """);

                        service.ExportCsv(csvPath, new[]
                        {
                                new ClassificationDocumentItem
                                {
                                        FileName = "doc-resumen.pdf",
                                Status = string.Empty,
                                TipologiaIdentificada = string.Empty,
                                ConfianzaGlobal = string.Empty,
                                        OutputJsonPath = jsonPath
                                }
                        });

                        var text = File.ReadAllText(csvPath, Encoding.UTF8);
                        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        Assert.True(lines.Length >= 2);

                        var row = lines[1].Split(';');
                        Assert.Equal("doc-resumen.pdf", row[0]);
                        Assert.Equal("VALIDACION_CON_ERRORES", row[1]);
                        Assert.Equal("escr.10", row[2]);
                        Assert.Equal("ESCR", row[3]);
                        Assert.Equal("1.0", row[4]);
                        Assert.Equal("1-3", row[6]);
                        Assert.Equal("5", row[7]);
                        Assert.Equal("ESCR", row[8]);
                        Assert.Equal("ESCR-10", row[9]);
                        Assert.Equal("M-0001", row[10]);
                        Assert.Equal("FoundryRescue", row[11]);
                        Assert.Equal("62,0 % (raw: 0.62)", row[12]);
                        Assert.Equal("true", row[13]);
                        Assert.Equal("4567", row[14]);
                        Assert.Equal("escr.10", row[15]);
                        Assert.Equal("0.81", row[16]);
                        Assert.Equal("none", row[17]);
                        Assert.Equal("escr.11", row[18]);
                        Assert.Equal("0.44", row[19]);
                        Assert.Equal("low-confidence", row[20]);
                        Assert.Equal("true", row[24]);
                }
                finally
                {
                        if (File.Exists(csvPath))
                        {
                                File.Delete(csvPath);
                        }

                        if (File.Exists(jsonPath))
                        {
                                File.Delete(jsonPath);
                        }
                }
        }
}
