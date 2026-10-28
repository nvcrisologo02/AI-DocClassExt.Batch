using System.Globalization;
using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Io;

/// <summary>
/// CSV de salida por documento. El texto copiado del documento se neutraliza frente a la
/// inyección de fórmulas de Excel: un valor que empieza por = + - @ tab CR o apóstrofo se escribe
/// con un apóstrofo delante, y Leer lo quita.
/// </summary>
public static class SalidaDocumentosCsv
{
    public static readonly string[] Cabecera =
    {
        "ID_SAREB", "Object ID del Documento", "TDN2", "Procesado", "EsTituloPrevio", "HuboIVA", "DeduccionIVA",
        "Cedente", "FechaAdquisicion", "Evidencia", "Confianza", "Tipologia", "InstanceId", "CorrelationId", "Error", "Fecha",
    };

    private const int MaxEvidencia = 4000;
    private const string SufijoTruncado = " [truncado]";

    public static void Anexar(string path, FilaDocumento fila)
        => CsvUtil.AppendRow(path, Cabecera, ALista(fila));

    public static void Escribir(string path, IEnumerable<FilaDocumento> filas)
        => CsvUtil.WriteRowsAtomic(path, Cabecera, filas.Select(ALista));

    public static List<FilaDocumento> Leer(string path)
    {
        var resultado = new List<FilaDocumento>();
        if (!File.Exists(path))
        {
            return resultado;
        }

        foreach (var c in CsvUtil.ReadRows(path).Skip(1))
        {
            string T(int i) => i < c.Length ? AntiFormula.Desneutralizar(c[i]) : "";
            var conf = T(10);
            var fecha = T(15);
            resultado.Add(new FilaDocumento
            {
                IdSareb = T(0),
                ObjectId = T(1),
                Tdn2 = T(2),
                Procesado = T(3),
                EsTituloPrevio = T(4),
                HuboIVA = T(5),
                DeduccionIVA = T(6),
                Cedente = T(7),
                FechaAdquisicion = T(8),
                Evidencia = T(9),
                Confianza = double.TryParse(conf, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
                Tipologia = T(11),
                InstanceId = T(12),
                CorrelationId = T(13),
                Error = T(14),
                Fecha = DateTime.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var f)
                    ? DateTime.SpecifyKind(f, DateTimeKind.Utc)
                    : null,
            });
        }

        return resultado;
    }

    private static string?[] ALista(FilaDocumento f)
    {
        var evidencia = f.Evidencia ?? "";
        if (evidencia.Length > MaxEvidencia)
        {
            evidencia = evidencia[..MaxEvidencia] + SufijoTruncado;
        }

        return new[]
        {
            AntiFormula.Neutralizar(f.IdSareb), AntiFormula.Neutralizar(f.ObjectId), AntiFormula.Neutralizar(f.Tdn2), AntiFormula.Neutralizar(f.Procesado),
            AntiFormula.Neutralizar(f.EsTituloPrevio), AntiFormula.Neutralizar(f.HuboIVA), AntiFormula.Neutralizar(f.DeduccionIVA),
            AntiFormula.Neutralizar(f.Cedente), AntiFormula.Neutralizar(f.FechaAdquisicion), AntiFormula.Neutralizar(evidencia),
            f.Confianza?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            AntiFormula.Neutralizar(f.Tipologia), AntiFormula.Neutralizar(f.InstanceId), AntiFormula.Neutralizar(f.CorrelationId), AntiFormula.Neutralizar(f.Error),
            f.Fecha?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? "",
        };
    }
}
