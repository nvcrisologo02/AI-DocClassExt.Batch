namespace DocumentIA.Batch.IvaCedente;

/// <summary>
/// Error esperado de uso o configuración (argumentos inválidos, fichero no encontrado). Se
/// distingue de las excepciones inesperadas para mostrar solo el mensaje y salir con código 1.
/// </summary>
public class IvaCedenteUsageException : Exception
{
    public IvaCedenteUsageException(string message) : base(message)
    {
    }
}
