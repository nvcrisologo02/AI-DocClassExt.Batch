using System.Globalization;
using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Io;

/// <summary>CSV de salida por activo. Las columnas de texto se neutralizan frente a fórmulas de Excel.</summary>
public static class SalidaActivosCsv
{
    public static readonly string[] Cabecera =
    {
        "ID_SAREB", "Documentos", "Leidos", "ConRespuesta", "HuboIVA", "DeduccionIVA",
        "DocIVA", "TipoIVA", "CuotaIVA", "Cedente", "FechaAdquisicion", "DocDeduccion", "Sustento", "MotivoRevisar",
        "ObjectIdsSoporte", "Revisar",
    };

    public static void Escribir(string path, IEnumerable<FilaActivo> filas)
        => CsvUtil.WriteRowsAtomic(path, Cabecera, filas.Select(f => (IEnumerable<string?>)new string?[]
        {
            AntiFormula.Neutralizar(f.IdSareb),
            f.Documentos.ToString(CultureInfo.InvariantCulture),
            f.Leidos.ToString(CultureInfo.InvariantCulture),
            f.ConRespuesta.ToString(CultureInfo.InvariantCulture),
            AntiFormula.Neutralizar(f.HuboIVA),
            AntiFormula.Neutralizar(f.DeduccionIVA),
            AntiFormula.Neutralizar(f.DocIVA),
            Numero(f.TipoIVA),
            Numero(f.CuotaIVA),
            AntiFormula.Neutralizar(f.Cedente),
            AntiFormula.Neutralizar(f.FechaAdquisicion),
            AntiFormula.Neutralizar(f.DocDeduccion),
            AntiFormula.Neutralizar(f.Sustento),
            AntiFormula.Neutralizar(f.MotivoRevisar),
            AntiFormula.Neutralizar(f.ObjectIdsSoporte),
            AntiFormula.Neutralizar(f.Revisar),
        }));

    private static string Numero(double? valor) => valor?.ToString(CultureInfo.InvariantCulture) ?? "";
}
