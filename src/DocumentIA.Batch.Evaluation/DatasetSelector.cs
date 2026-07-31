using System.Security.Cryptography;
using System.Text;
using DocumentIA.Batch.Evaluation.Models;

namespace DocumentIA.Batch.Evaluation;

public enum EvaluationSet
{
    Golden,
    Full,
    HalfA,
    HalfB,
    Cata100
}

/// <summary>
/// Selecciona el subconjunto de documentos a evaluar segun el --set pedido. "full" es el
/// universo validado fuera de Cata_100; "half-a"/"half-b" son mitades deterministas de "full"
/// (sin RNG, reproducibles entre ejecuciones) partidas por la paridad del primer byte del SHA1
/// del nombre de fichero.
/// </summary>
public static class DatasetSelector
{
    public static List<ManifestRow> Full(IReadOnlyList<ManifestRow> manifestRows)
        => manifestRows.Where(r => r.Validated && !r.InCata100).ToList();

    public static List<ManifestRow> Cata100(IReadOnlyList<ManifestRow> manifestRows)
        => manifestRows.Where(r => r.InCata100 && r.Validated).ToList();

    public static List<ManifestRow> HalfA(IReadOnlyList<ManifestRow> fullRows)
        => fullRows.Where(r => FirstShaByte(r.FileName) % 2 == 0).ToList();

    public static List<ManifestRow> HalfB(IReadOnlyList<ManifestRow> fullRows)
        => fullRows.Where(r => FirstShaByte(r.FileName) % 2 != 0).ToList();

    public static byte FirstShaByte(string fileName)
        => SHA1.HashData(Encoding.UTF8.GetBytes(fileName))[0];

    public static EvaluationSet ParseSet(string value) => value.Trim().ToLowerInvariant() switch
    {
        "golden" => EvaluationSet.Golden,
        "full" => EvaluationSet.Full,
        "half-a" => EvaluationSet.HalfA,
        "half-b" => EvaluationSet.HalfB,
        "cata100" => EvaluationSet.Cata100,
        _ => throw new EvaluationUsageException($"--set desconocido: '{value}'. Valores validos: golden, full, half-a, half-b, cata100.")
    };
}
