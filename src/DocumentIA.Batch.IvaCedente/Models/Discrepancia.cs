namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>
/// Título de un activo cuyo valor difiere del título elegido. <see cref="HuboIVA"/> nulo: no discrepa en IVA;
/// <see cref="DeduccionIVA"/> nulo: no discrepa en deducción. Tipo y cuota solo cuando <see cref="HuboIVA"/> es sí.
/// </summary>
public sealed record Discrepancia(string ObjectId, string? HuboIVA, double? TipoIVA, double? CuotaIVA, string? DeduccionIVA);
