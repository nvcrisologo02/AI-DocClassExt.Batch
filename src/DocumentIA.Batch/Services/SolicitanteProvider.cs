namespace DocumentIA.Batch.Services;

/// <summary>
/// Compone el valor que viaja en trazabilidad.submittedBy: el programa que lanza la
/// petición y el usuario que lo está usando, en un único campo "programa/usuario".
/// El usuario se resuelve solo, sin pedírselo, y se cachea una vez por proceso.
/// </summary>
public static class SolicitanteProvider
{
    /// <summary>Longitud de Documentos.SubmittedBy y DocumentoEjecuciones.SubmittedBy.</summary>
    public const int LongitudMaxima = 200;

    public const char Separador = '/';

    /// <summary>
    /// Tope para la resolución del UPN. GetUserNameEx puede necesitar el controlador de
    /// dominio, así que se acota para no bloquear el arranque en un equipo sin red corporativa.
    /// </summary>
    public static readonly TimeSpan TimeoutResolucion = TimeSpan.FromSeconds(2);

    private static readonly Lazy<string?> UsuarioCacheado =
        new(() => ResolverUsuario(new IdentidadWindows(), TimeoutResolucion));

    /// <summary>Usuario resuelto para este proceso. Null si no se pudo averiguar.</summary>
    public static string? Usuario => UsuarioCacheado.Value;

    /// <summary>
    /// Valor completo para submittedBy. Si no hay usuario, queda solo el programa.
    /// </summary>
    public static string ObtenerSolicitante(string programa) => Componer(programa, Usuario);

    /// <summary>
    /// Normaliza lo que el usuario teclea en el campo editable: si lo deja vacío se restaura
    /// el valor calculado, en otro caso se respeta lo escrito sin espacios sobrantes.
    /// </summary>
    public static string NormalizarEdicion(string programa, string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? ObtenerSolicitante(programa) : valor.Trim();

    /// <summary>
    /// Une programa y usuario respetando el límite de la columna. Si hay que recortar se
    /// recorta el usuario: el programa siempre viaja entero.
    /// </summary>
    public static string Componer(string programa, string? usuario)
    {
        var programaNormalizado = (programa ?? string.Empty).Trim();
        if (programaNormalizado.Length >= LongitudMaxima)
        {
            return programaNormalizado[..LongitudMaxima];
        }

        var usuarioNormalizado = usuario?.Trim();
        if (string.IsNullOrEmpty(usuarioNormalizado))
        {
            return programaNormalizado;
        }

        if (programaNormalizado.Length == 0)
        {
            return usuarioNormalizado.Length > LongitudMaxima
                ? usuarioNormalizado[..LongitudMaxima]
                : usuarioNormalizado;
        }

        var espacioParaUsuario = LongitudMaxima - programaNormalizado.Length - 1;
        if (espacioParaUsuario <= 0)
        {
            return programaNormalizado;
        }

        if (usuarioNormalizado.Length > espacioParaUsuario)
        {
            usuarioNormalizado = usuarioNormalizado[..espacioParaUsuario];
        }

        return $"{programaNormalizado}{Separador}{usuarioNormalizado}";
    }

    /// <summary>
    /// Cascada de resolución: UPN, cuenta Windows y, como último recurso, nombre de sesión.
    /// </summary>
    internal static string? ResolverUsuario(IIdentidadWindows identidad, TimeSpan timeout)
    {
        var upn = EjecutarConTimeout(identidad.ObtenerUpn, timeout);
        if (!string.IsNullOrWhiteSpace(upn))
        {
            return upn.Trim();
        }

        var cuenta = EjecutarSeguro(identidad.ObtenerCuentaWindows);
        if (!string.IsNullOrWhiteSpace(cuenta))
        {
            return cuenta.Trim();
        }

        var nombre = EjecutarSeguro(identidad.ObtenerNombreUsuario);
        return string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
    }

    private static string? EjecutarConTimeout(Func<string?> operacion, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return EjecutarSeguro(operacion);
        }

        try
        {
            var tarea = Task.Run(operacion);
            // Si expira dejamos la tarea corriendo en segundo plano y seguimos con la cascada.
            return tarea.Wait(timeout) ? tarea.Result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? EjecutarSeguro(Func<string?> operacion)
    {
        try
        {
            return operacion();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
