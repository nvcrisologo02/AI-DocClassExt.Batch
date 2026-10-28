using System.Globalization;
using DocumentIA.Batch.IvaCedente.Models;

namespace DocumentIA.Batch.IvaCedente.Io;

/// <summary>CSV de salida por activo. Sin texto libre, por lo que no neutraliza fórmulas.</summary>
public static class SalidaActivosCsv
{
    public static readonly string[] Cabecera =
    {
        "ID_SAREB", "Documentos", "Leidos", "ConRespuesta", "HuboIVA", "DeduccionIVA", "ObjectIdsSoporte", "Revisar",
    };

    public static void Escribir(string path, IEnumerable<FilaActivo> filas)
        => CsvUtil.WriteRows(path, Cabecera, filas.Select(f => (IEnumerable<string?>)new string?[]
        {
            f.IdSareb,
            f.Documentos.ToString(CultureInfo.InvariantCulture),
            f.Leidos.ToString(CultureInfo.InvariantCulture),
            f.ConRespuesta.ToString(CultureInfo.InvariantCulture),
            f.HuboIVA,
            f.DeduccionIVA,
            f.ObjectIdsSoporte,
            f.Revisar,
        }));
}
