using IPhoneMirror.HidProbe.Ble;
using IPhoneMirror.HidProbe.Diagnostics;
using IPhoneMirror.HidProbe.UI;

namespace IPhoneMirror.HidProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Log.Initialize("hidprobe");

        // Whatever happens, never leave Bluetooth Classic switched off.
        Application.ThreadException += (_, e) =>
        {
            Log.Error("Unhandled UI exception", e.Exception);
            BrEdrSuppressor.Restore();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error($"Unhandled exception (terminating: {e.IsTerminating}): {e.ExceptionObject}");
            BrEdrSuppressor.Restore();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => BrEdrSuppressor.Restore();
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        BrEdrSuppressor.RecoverFromPreviousRun();

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
