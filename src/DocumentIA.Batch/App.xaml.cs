using System.Diagnostics;
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
        TriggerStartupBreakpointIfRequested();
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

    [Conditional("DEBUG")]
    private static void TriggerStartupBreakpointIfRequested()
    {
        // Activar con: setx DOCIA_BATCH_DEBUG_BREAK_ON_STARTUP 1 (o en la sesión actual)
        var shouldBreak = Environment.GetEnvironmentVariable("DOCIA_BATCH_DEBUG_BREAK_ON_STARTUP");
        if (!string.Equals(shouldBreak, "1", StringComparison.Ordinal))
        {
            return;
        }

        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }

        Debugger.Break();
    }
}

