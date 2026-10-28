namespace DocumentIA.Batch.IvaCedente.Models;

/// <summary>Valores fijos de las columnas huboIVA y deduccionIVA.</summary>
public static class ValoresIva
{
    public const string Si = "sí";
    public const string No = "no";
    public const string NoConsta = "no consta";
    public const string NoEsTitulo = "no es título";
    public const string NoAplica = "no aplica";
}

/// <summary>Estados de procesado de una fila en el CSV de salida.</summary>
public static class EstadosProcesado
{
    public const string Pendiente = "pendiente";
    public const string NoAplica = "no aplica";
    public const string NoEsTitulo = "no es título";
    public const string Ok = "OK";
    public const string Error = "error";
    public const string Timeout = "timeout";
    public const string SinContenido = "sin contenido";
    public const string NoParseable = "respuesta no parseable";

    /// <summary>Un estado es final si la fila no se reintenta: todo salvo pendiente, error y timeout.</summary>
    public static bool EsFinal(string? estado)
        => estado is not (null or Pendiente or Error or Timeout);
}
