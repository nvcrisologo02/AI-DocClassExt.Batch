using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace DocumentIA.Batch.Services;

/// <summary>
/// Acceso a la identidad del usuario que ejecuta el proceso. Se aísla en una interfaz
/// para poder probar la cascada de resolución sin depender del equipo donde corren los tests.
/// </summary>
public interface IIdentidadWindows
{
    /// <summary>UPN del usuario (nombre.apellido@dominio). Null si no se puede resolver.</summary>
    string? ObtenerUpn();

    /// <summary>Cuenta Windows en formato DOMINIO\usuario. Null si no se puede resolver.</summary>
    string? ObtenerCuentaWindows();

    /// <summary>Nombre de inicio de sesión sin dominio. Null si no se puede resolver.</summary>
    string? ObtenerNombreUsuario();
}

/// <summary>
/// Implementación real sobre las API de Windows.
/// </summary>
public sealed class IdentidadWindows : IIdentidadWindows
{
    // EXTENDED_NAME_FORMAT.NameUserPrincipal (secext.h)
    private const int NameUserPrincipal = 8;
    private const int ErrorMoreData = 234;
    private const int CapacidadInicial = 256;

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool GetUserNameExW(int nameFormat, StringBuilder? lpNameBuffer, ref uint nSize);

    /// <summary>
    /// GetUserNameEx con NameUserPrincipal. Puede fallar con ERROR_NONE_MAPPED (equipo fuera
    /// de dominio) o ERROR_NO_SUCH_DOMAIN (controlador de dominio inalcanzable); en ambos casos
    /// devolvemos null para que el llamante siga bajando por la cascada.
    /// </summary>
    public string? ObtenerUpn()
    {
        try
        {
            var tamano = (uint)CapacidadInicial;
            var buffer = new StringBuilder(CapacidadInicial);

            if (GetUserNameExW(NameUserPrincipal, buffer, ref tamano))
            {
                return buffer.ToString();
            }

            // El buffer se queda corto: tamano trae los caracteres necesarios, incluido el nulo.
            if (Marshal.GetLastWin32Error() != ErrorMoreData || tamano == 0)
            {
                return null;
            }

            buffer = new StringBuilder((int)tamano);
            return GetUserNameExW(NameUserPrincipal, buffer, ref tamano)
                ? buffer.ToString()
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public string? ObtenerCuentaWindows()
    {
        try
        {
            using var identidad = WindowsIdentity.GetCurrent();
            return identidad.Name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? ObtenerNombreUsuario()
    {
        try
        {
            return Environment.UserName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
