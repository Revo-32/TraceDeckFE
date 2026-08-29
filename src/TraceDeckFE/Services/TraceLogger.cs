using System.Text;

namespace TraceDeckFE.Services;

public sealed class TraceLogger : ITraceLogger
{
    private readonly object _gate = new();
    private readonly string? _logPath;

    public TraceLogger(IApplicationPaths? paths = null)
    {
        try
        {
            var logDirectory = Path.Combine((paths ?? new PortableApplicationPaths()).DataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            _logPath = Path.Combine(logDirectory, $"tracedeck-{DateTime.Now:yyyyMMdd}.log");
        }
        catch
        {
            _logPath = null;
        }
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        if (_logPath is null)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [").Append(level).Append("] ")
                .Append(message);

            if (exception is not null)
            {
                builder.AppendLine().Append(exception);
            }

            builder.AppendLine();
            lock (_gate)
            {
                File.AppendAllText(_logPath, builder.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never take down the controller.
        }
    }
}
