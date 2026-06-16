using System.IO;
using System.Text;

namespace BookOfTheEnd.Services;

/// <summary>
/// Minimal thread-safe file logger. Logs and recovery reports are written under
/// %LOCALAPPDATA%\BookOfTheEnd\logs so the app stays fully offline and local.
/// </summary>
public sealed class LoggingService
{
    private readonly object _gate = new();
    private readonly string _logFile;

    public string LogDirectory { get; }

    public LoggingService()
    {
        LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BookOfTheEnd", "logs");
        Directory.CreateDirectory(LogDirectory);
        _logFile = Path.Combine(LogDirectory, $"session-{DateTime.Now:yyyyMMdd}.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex}");

    private void Write(string level, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        try
        {
            lock (_gate)
                File.AppendAllText(_logFile, line, Encoding.UTF8);
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    /// <summary>Writes a recovery report file and returns its path.</summary>
    public string WriteRecoveryReport(string content)
    {
        string reportDir = Path.Combine(LogDirectory, "..", "reports");
        Directory.CreateDirectory(reportDir);
        string path = Path.GetFullPath(Path.Combine(reportDir, $"recovery-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
        try
        {
            File.WriteAllText(path, content, Encoding.UTF8);
            Info($"Recovery report written to {path}");
        }
        catch (Exception ex)
        {
            Error("Failed to write recovery report", ex);
        }
        return path;
    }
}
