using System.Diagnostics;
using TraceDeckFE.Interop;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public sealed class ForzaWindowTracker : IDisposable
{
    private readonly object _gate = new();
    private readonly ITraceLogger _logger;
    private readonly NativeMethods.WinEventDelegate _eventCallback;
    private readonly List<nint> _hooks = [];
    private nint _targetHandle;
    private bool _disposed;

    public ForzaWindowTracker(ITraceLogger logger)
    {
        _logger = logger;
        _eventCallback = OnWinEvent;
    }

    public event EventHandler<TargetWindowSnapshot>? StateChanged;
    public event EventHandler? ConnectionLost;

    public nint TargetHandle
    {
        get
        {
            lock (_gate)
            {
                return _targetHandle;
            }
        }
    }

    public bool IsConnected => TargetHandle != 0 && NativeMethods.IsWindow(TargetHandle);

    public bool Attach(nint handle)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle))
        {
            return false;
        }

        lock (_gate)
        {
            ThrowIfDisposed();
            RemoveHooks();
            _targetHandle = handle;
            InstallHooks();
        }

        _logger.Info($"Attached target window 0x{handle:X}.");
        PublishCurrentState();
        return true;
    }

    public void Verify()
    {
        var handle = TargetHandle;
        if (handle == 0)
        {
            return;
        }

        if (!NativeMethods.IsWindow(handle))
        {
            Disconnect(notifyLost: true);
            return;
        }

        PublishCurrentState();
    }

    public void Disconnect(bool notifyLost = false)
    {
        bool hadTarget;
        lock (_gate)
        {
            hadTarget = _targetHandle != 0;
            _targetHandle = 0;
            RemoveHooks();
        }

        if (!hadTarget)
        {
            return;
        }

        StateChanged?.Invoke(this, TargetWindowSnapshot.Disconnected);
        if (notifyLost)
        {
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }
    }

    public TargetWindowSnapshot ReadCurrentState()
    {
        var handle = TargetHandle;
        if (handle == 0 || !NativeMethods.IsWindow(handle))
        {
            return TargetWindowSnapshot.Disconnected;
        }

        var title = NativeMethods.ReadWindowTitle(handle);
        var processName = ReadProcessName(handle);
        _ = NativeMethods.TryGetClientBounds(handle, out var clientBounds);

        return new TargetWindowSnapshot(
            handle,
            title,
            processName,
            clientBounds,
            Exists: true,
            IsVisible: NativeMethods.IsWindowVisible(handle),
            IsMinimized: NativeMethods.IsIconic(handle));
    }

    private void InstallHooks()
    {
        _ = NativeMethods.GetWindowThreadProcessId(_targetHandle, out var targetProcessId);
        AddHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground, 0, skipOwnProcess: false);
        AddHook(NativeMethods.EventSystemMinimizeStart, NativeMethods.EventSystemMinimizeEnd, targetProcessId, skipOwnProcess: true);
        AddHook(NativeMethods.EventObjectDestroy, NativeMethods.EventObjectLocationChange, targetProcessId, skipOwnProcess: true);
    }

    private void AddHook(uint eventMin, uint eventMax, uint processId, bool skipOwnProcess)
    {
        var hook = NativeMethods.SetWinEventHook(
            eventMin,
            eventMax,
            0,
            _eventCallback,
            processId,
            0,
            NativeMethods.WineventOutOfContext |
            (skipOwnProcess ? NativeMethods.WineventSkipOwnProcess : 0));

        if (hook == 0)
        {
            _logger.Warning($"SetWinEventHook failed for 0x{eventMin:X}-0x{eventMax:X}.");
            return;
        }

        _hooks.Add(hook);
    }

    private void RemoveHooks()
    {
        foreach (var hook in _hooks)
        {
            _ = NativeMethods.UnhookWinEvent(hook);
        }

        _hooks.Clear();
    }

    private void OnWinEvent(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        var target = TargetHandle;
        if (target == 0)
        {
            return;
        }

        if (eventType == NativeMethods.EventSystemForeground)
        {
            PublishCurrentState();
            return;
        }

        if (hwnd != target || (idObject != NativeMethods.ObjIdWindow && idObject != 0))
        {
            return;
        }

        if (eventType == NativeMethods.EventObjectDestroy || !NativeMethods.IsWindow(target))
        {
            Disconnect(notifyLost: true);
            return;
        }

        PublishCurrentState();
    }

    private void PublishCurrentState() => StateChanged?.Invoke(this, ReadCurrentState());

    private static string ReadProcessName(nint handle)
    {
        try
        {
            _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            using var process = Process.GetProcessById(unchecked((int)processId));
            return process.ProcessName;
        }
        catch
        {
            return "Unknown process";
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_gate)
        {
            _disposed = true;
            _targetHandle = 0;
            RemoveHooks();
        }
    }
}
