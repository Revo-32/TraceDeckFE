using System.Diagnostics;
using TraceDeckFE.Interop;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed class WindowCatalog
{
    private const int MinimumWindowWidth = 160;
    private const int MinimumWindowHeight = 120;
    private readonly ITraceLogger _logger;

    public WindowCatalog(ITraceLogger logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<WindowInfo> EnumerateCandidateWindows()
    {
        var windows = new List<WindowInfo>();
        var currentProcessId = Environment.ProcessId;

        NativeMethods.EnumWindows((handle, ignoredParameter) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsCloaked(handle))
                {
                    return true;
                }

                var title = NativeMethods.ReadWindowTitle(handle);
                if (string.IsNullOrWhiteSpace(title) ||
                    !NativeMethods.TryGetClientBounds(handle, out var bounds) ||
                    bounds.Width < MinimumWindowWidth ||
                    bounds.Height < MinimumWindowHeight)
                {
                    return true;
                }

                var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
                if ((exStyle & NativeMethods.WsExToolWindow) != 0)
                {
                    return true;
                }

                _ = NativeMethods.GetWindowThreadProcessId(handle, out var rawProcessId);
                var processId = unchecked((int)rawProcessId);
                if (processId == 0 || processId == currentProcessId)
                {
                    return true;
                }

                var processName = ReadProcessName(processId);
                windows.Add(new WindowInfo(handle, processId, processName, title, bounds));
            }
            catch (Exception exception)
            {
                _logger.Warning($"Skipped a window during enumeration: {exception.Message}");
            }

            return true;
        }, 0);

        return windows
            .OrderByDescending(ScoreForzaCandidate)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public WindowInfo? FindForzaHorizon6()
    {
        var candidate = EnumerateCandidateWindows()
            .Select(window => (Window: window, Score: ScoreForzaCandidate(window)))
            .FirstOrDefault(item => item.Score >= 70);

        return candidate.Score >= 70 ? candidate.Window : null;
    }

    internal static int ScoreForzaCandidate(WindowInfo window)
    {
        static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        var title = Normalize(window.Title);
        var process = Normalize(window.ProcessName);
        var score = 0;

        if (title.Contains("forzahorizon6", StringComparison.Ordinal))
        {
            score += 100;
        }
        else if (title.Contains("forza", StringComparison.Ordinal) && title.Contains("horizon", StringComparison.Ordinal))
        {
            score += 70;
        }

        if (process.Contains("forzahorizon6", StringComparison.Ordinal))
        {
            score += 90;
        }
        else if (process.Contains("forza", StringComparison.Ordinal) && process.Contains("horizon", StringComparison.Ordinal))
        {
            score += 60;
        }
        else if (process is "fh6")
        {
            score += 80;
        }

        if (window.ClientBounds.Width >= 1280 && window.ClientBounds.Height >= 720)
        {
            score += 5;
        }

        return score;
    }

    private static string ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown process";
        }
    }
}
