namespace DocumentIA.Batch.Evaluation;

/// <summary>
/// Error esperado de uso/configuracion (argumentos invalidos, Function Key ausente, fichero no
/// encontrado). Se distingue de excepciones inesperadas para mostrar solo el mensaje al usuario,
/// sin traza, y salir con codigo 1.
/// </summary>
public class EvaluationUsageException : Exception
{
    public EvaluationUsageException(string message) : base(message)
    {
    }
}
