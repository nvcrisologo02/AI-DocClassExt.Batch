namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Una fila del CSV de entrada: activo, documento y TDN2 ya normalizados.</summary>
public sealed record FilaEntrada(string IdSareb, string ObjectId, string Tdn2);
