namespace TornCommander;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        TraceStartup("Process entered Main().");
        ApplicationConfiguration.Initialize();
        TraceStartup("WinForms configuration initialized.");

        Application.ThreadException += (_, e) => ReportCrash("UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ReportCrash("Unhandled", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "Unknown fatal error"));

        try
        {
            TraceStartup("Creating Commander main window.");
            using var mainWindow = new MainForm();
            TraceStartup("Main window created; entering message loop.");
            Application.Run(mainWindow);
            TraceStartup("Message loop ended normally.");
        }
        catch (Exception ex)
        {
            TraceStartup("Fatal startup exception: " + ex.GetType().Name + ": " + ex.Message);
            ReportCrash("Startup", ex);
        }
    }

    internal static void TraceStartup(string message)
    {
        // Never put API keys, cookies or request URLs in a startup log.
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TornCommander");
            Directory.CreateDirectory(root);
            File.AppendAllText(Path.Combine(root, "startup-diagnostic.log"),
                $"[{DateTime.Now:O}] v0.6.13 PID={Environment.ProcessId} {message}\r\n");
        }
        catch { /* Diagnostics must never prevent startup. */ }
    }

    private static void ReportCrash(string stage, Exception ex)
    {
        TraceStartup("Crash handler: " + stage + ": " + ex.GetType().Name + ": " + ex.Message);
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TornCommander");
            Directory.CreateDirectory(root);
            var log = Path.Combine(root, "startup-crash.log");
            File.AppendAllText(log,
                $"[{DateTime.Now:O}] {stage}\r\n{ex}\r\n----------------------------------------\r\n");

            MessageBox.Show(
                $"Torn Commander hit an unexpected {stage.ToLowerInvariant()} error.\n\n" +
                $"A crash log was written to:\n{log}\n\n" +
                $"Error: {ex.Message}",
                "Torn Commander error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch
        {
            try
            {
                MessageBox.Show(ex.ToString(), "Torn Commander fatal error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
