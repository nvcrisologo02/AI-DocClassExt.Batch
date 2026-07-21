using System.Runtime.InteropServices;

namespace DocumentIA.Batch.ClassificationLite.Services;

/// <summary>
/// Impide la suspension del equipo durante ejecuciones desatendidas (la pantalla si puede apagarse).
/// </summary>
public static class SleepBlocker
{
    [Flags]
    private enum ExecutionState : uint
    {
        Continuous = 0x80000000,
        SystemRequired = 0x00000001
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(ExecutionState flags);

    public static void PreventSleep()
        => SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired);

    public static void AllowSleep()
        => SetThreadExecutionState(ExecutionState.Continuous);
}
