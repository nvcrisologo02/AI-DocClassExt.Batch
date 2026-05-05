using System.Windows;
using DocumentIA.Batch.Services;

namespace DocumentIA.Batch;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Inicializar el servicio de historial (best-effort)
        try
        {
            var historialService = new BatchHistorialService();
            _ = historialService.InitializeAsync();
        }
        catch (Exception ex)
        {
            // El historial es opcional; no interrumpe el startup
            System.Diagnostics.Debug.WriteLine($"Error inicializando historial: {ex.Message}");
        }
    }
}

