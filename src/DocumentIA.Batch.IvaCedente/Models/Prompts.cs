namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Textos de los prompts de sistema y de usuario ya leídos de disco.</summary>
public sealed record Prompts(string System, string User);
