using System.Text;

namespace IPhoneMirror.HidProbe.Diagnostics;

internal enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Minimal thread-safe file logger. Every line is written to the log file;
/// lines at <see cref="LogLevel.Info"/> or above are also raised through
/// <see cref="LineLogged"/> so the UI can show them.
/// </summary>
internal static class Log
{
    private static readonly object s_lock = new();
    private static StreamWriter? s_writer;

    public static string LogDirectory { get; } = Path.Combine(Path.GetTempPath(), "iPhoneMirror");

    public static string? FilePath { get; private set; }

    public static event Action<LogLevel, string>? LineLogged;

    public static void Initialize(string prefix)
    {
        Directory.CreateDirectory(LogDirectory);
        FilePath = Path.Combine(LogDirectory, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        s_writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public static void Debug(string message) => Write(LogLevel.Debug, message);

    public static void Info(string message) => Write(LogLevel.Info, message);

    public static void Warn(string message) => Write(LogLevel.Warn, message);

    public static void Error(string message) => Write(LogLevel.Error, message);

    public static void Error(string message, Exception ex) =>
        Write(LogLevel.Error, $"{message}: {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}{Environment.NewLine}{ex}");

    private static void Write(LogLevel level, string message)
    {
        var tag = level switch
        {
            LogLevel.Debug => "DBG",
            LogLevel.Info => "INF",
            LogLevel.Warn => "WRN",
            _ => "ERR",
        };
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{tag}] {message}";

        lock (s_lock)
        {
            try
            {
                s_writer?.WriteLine(line);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }

        System.Diagnostics.Debug.WriteLine(line);
        if (level >= LogLevel.Info)
        {
            LineLogged?.Invoke(level, line);
        }
    }
}
