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
        "Cedente", "FechaAdquisicion", "TipoIVA", "CuotaIVA", "Evidencia", "Confianza", "Tipologia", "InstanceId", "CorrelationId", "Error", "Fecha",
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

        var filas = CsvUtil.ReadRows(path);
        if (filas.Count == 0)
        {
            return resultado;
        }

        // Las columnas se localizan por su nombre de cabecera: una salida escrita por una versión
        // anterior, sin alguna columna, se sigue leyendo (la columna ausente queda vacía).
        var indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < filas[0].Length; i++)
        {
            indice[filas[0][i].Trim((char)0xFEFF, ' ')] = i;
        }

        foreach (var c in filas.Skip(1))
        {
            string T(string columna) => indice.TryGetValue(columna, out var i) && i < c.Length ? AntiFormula.Desneutralizar(c[i]) : "";
            var fecha = T("Fecha");
            resultado.Add(new FilaDocumento
            {
                IdSareb = T("ID_SAREB"),
                ObjectId = T("Object ID del Documento"),
                Tdn2 = T("TDN2"),
                Procesado = T("Procesado"),
                EsTituloPrevio = T("EsTituloPrevio"),
                HuboIVA = T("HuboIVA"),
                DeduccionIVA = T("DeduccionIVA"),
                Cedente = T("Cedente"),
                FechaAdquisicion = T("FechaAdquisicion"),
                TipoIVA = Numero(T("TipoIVA")),
                CuotaIVA = Numero(T("CuotaIVA")),
                Evidencia = T("Evidencia"),
                Confianza = Numero(T("Confianza")),
                Tipologia = T("Tipologia"),
                InstanceId = T("InstanceId"),
                CorrelationId = T("CorrelationId"),
                Error = T("Error"),
                Fecha = DateTime.TryParse(fecha, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var f)
                    ? DateTime.SpecifyKind(f, DateTimeKind.Utc)
                    : null,
            });
        }

        return resultado;
    }

    private static double? Numero(string texto)
        => double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Numero(double? valor)
        => valor?.ToString("R", CultureInfo.InvariantCulture) ?? "";

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
            AntiFormula.Neutralizar(f.Cedente), AntiFormula.Neutralizar(f.FechaAdquisicion), Numero(f.TipoIVA), Numero(f.CuotaIVA), AntiFormula.Neutralizar(evidencia),
            Numero(f.Confianza),
            AntiFormula.Neutralizar(f.Tipologia), AntiFormula.Neutralizar(f.InstanceId), AntiFormula.Neutralizar(f.CorrelationId), AntiFormula.Neutralizar(f.Error),
            f.Fecha?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) ?? "",
        };
    }
}
