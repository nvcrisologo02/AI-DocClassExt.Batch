using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace DocumentIA.Batch.ClassificationLite.Services;

public static class ManualLauncher
{
    private const string ResourceSuffix = "MANUAL-USUARIO.html";

    /// <summary>
    /// Extrae el manual embebido a un fichero temporal estable y devuelve su ruta.
    /// Sobrescribe en cada llamada para que refleje siempre la versión embebida actual.
    /// </summary>
    public static string ExtractManual()
    {
        var assembly = typeof(ManualLauncher).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No se encontró el recurso embebido del manual (" + ResourceSuffix + ").");

        var targetDir = Path.Combine(Path.GetTempPath(), "DocumentIA.BatchLite");
        Directory.CreateDirectory(targetDir);
        var targetPath = Path.Combine(targetDir, ResourceSuffix);

        using (var input = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("No se pudo abrir el recurso embebido del manual."))
        using (var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write))
        {
            input.CopyTo(output);
        }

        return targetPath;
    }

    /// <summary>Extrae el manual y lo abre con el navegador/visor por defecto del sistema.</summary>
    public static void Open()
    {
        var path = ExtractManual();
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
