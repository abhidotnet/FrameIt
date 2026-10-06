using System.Globalization;
using System.IO;
using System.Text;

namespace FrameIt.Services;

public sealed class TimingLogger
{
    private readonly string _logFilePath;
    private readonly object _gate = new();

    public TimingLogger(string settingsDirectory)
    {
        _logFilePath = Path.Combine(settingsDirectory, "timings.log");
    }

    public void LogStartup(TimeSpan elapsed)
    {
        WriteLine($"startup-ready-ms={elapsed.TotalMilliseconds:F1}");
    }

    public void LogCapture(string mode, TimeSpan captureElapsed, TimeSpan totalElapsed, string provider)
    {
        WriteLine(
            $"capture mode={mode} provider={provider} capture-ms={captureElapsed.TotalMilliseconds:F1} total-ms={totalElapsed.TotalMilliseconds:F1}");
    }

    private void WriteLine(string message)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");

        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
            File.AppendAllText(_logFilePath, line, Encoding.UTF8);
        }
    }
}
