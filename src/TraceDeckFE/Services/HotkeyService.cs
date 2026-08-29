using System.Windows.Input;
using TraceDeckFE.Localization;
using System.Windows.Interop;
using System.Windows.Threading;
using TraceDeckFE.Interop;
using TraceDeckFE.Models;

namespace TraceDeckFE.Services;

public interface IHotkeyRegistrar
{
    bool Register(int id, ShortcutBinding binding);
    void Unregister(int id);
}
public sealed class HotkeyService(IHotkeyRegistrar registrar) : IDisposable
{
    private readonly Dictionary<int, ShortcutBinding> _registered = [];
    private List<ShortcutBinding> _bindings = ShortcutCatalog.Defaults();
    public bool IsActive { get; private set; }
    public IReadOnlyList<string> Conflicts { get; private set; } = [];
    public event EventHandler? StatusChanged;
    public void Configure(IEnumerable<ShortcutBinding> bindings)
    {
        _bindings = bindings.Where(b => b.IsGlobal).ToList();
        Refresh();
    }
    public void SetContext(bool active)
    {
        if (active == IsActive) return;
        IsActive = active;
        Refresh();
    }
    public ShortcutAction? Resolve(int id) => IsActive && _registered.TryGetValue(id, out var b) ? b.Action : null;
    private void Refresh()
    {
        foreach (var id in _registered.Keys) registrar.Unregister(id);
        _registered.Clear();
        var conflicts = new List<string>();
        if (IsActive)
        {
            foreach (var binding in _bindings.Where(b => b.IsGlobal))
            {
                var id = 0x6000 + (int)binding.Action;
                var error = ShortcutCatalog.Validate(binding, _bindings);
                if (error is null && registrar.Register(id, binding)) _registered.Add(id, binding);
                else conflicts.Add(error ?? L.Format("Shortcut.InUse", binding.Gesture));
            }
        }
        Conflicts = conflicts;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose() { IsActive = false; Refresh(); }
}

/// <summary>Foreground event hook only; no polling, input injection, or game process access.</summary>
public sealed class NativeHotkeyHost : IHotkeyRegistrar, IDisposable
{
    private readonly nint _controller;
    private readonly Func<nint> _target;
    private readonly Func<bool> _enabled;
    private readonly Dispatcher _dispatcher;
    private readonly NativeMethods.WinEventDelegate _foregroundCallback;
    private readonly HwndSource? _source;
    private nint _hook;
    private bool _disposed;
    public HotkeyService Service { get; }
    public event EventHandler<ShortcutAction>? Invoked;
    public NativeHotkeyHost(nint controller, Func<nint> target, Func<bool> enabled, Dispatcher dispatcher)
    {
        _controller = controller; _target = target; _enabled = enabled; _dispatcher = dispatcher;
        Service = new(this);
        _source = HwndSource.FromHwnd(controller);
        _source?.AddHook(WindowProcedure);
        _foregroundCallback = (_, _, _, _, _, _, _) => _dispatcher.BeginInvoke(RefreshContext);
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EventSystemForeground, NativeMethods.EventSystemForeground, 0,
            _foregroundCallback, 0, 0, NativeMethods.WineventOutOfContext);
        RefreshContext();
    }
    public bool HasForegroundHook => _hook != 0;
    public void RefreshContext()
    {
        if (_disposed) return;
        var foreground = NativeMethods.GetForegroundWindow();
        Service.SetContext(_enabled() && (foreground == _controller || _target() != 0 && foreground == _target()));
    }
    public bool Register(int id, ShortcutBinding binding)
    {
        var modifiers = (uint)binding.Modifiers; // WPF and Win32 use ALT=1, CTRL=2, SHIFT=4, WIN=8.
        return NativeMethods.RegisterHotKey(_controller, id, modifiers | NativeMethods.ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(binding.Key));
    }
    public void Unregister(int id) => NativeMethods.UnregisterHotKey(_controller, id);
    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotkey)
        {
            RefreshContext(); // Ignore a queued message if focus has already left the work context.
            if (Service.Resolve((int)wParam) is { } action) { handled = true; Invoked?.Invoke(this, action); }
        }
        return 0;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != 0) NativeMethods.UnhookWinEvent(_hook);
        _hook = 0; Service.Dispose(); _source?.RemoveHook(WindowProcedure);
    }
}
