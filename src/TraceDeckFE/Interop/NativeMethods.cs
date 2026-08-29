using System.Runtime.InteropServices;
using System.Text;
using TraceDeckFE.Models;

namespace TraceDeckFE.Interop;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const int GwlHwndParent = -8;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;

    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const int SwHide = 0;
    internal const int SwShowNoActivate = 4;
    internal const int WmHotkey = 0x0312;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VkEscape = 0x1B;
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSysKeyDown = 0x0104;

    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventSystemMinimizeStart = 0x0016;
    internal const uint EventSystemMinimizeEnd = 0x0017;
    internal const uint EventObjectDestroy = 0x8001;
    internal const uint EventObjectShow = 0x8002;
    internal const uint EventObjectHide = 0x8003;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint WineventOutOfContext = 0x0000;
    internal const uint WineventSkipOwnProcess = 0x0002;
    internal const int ObjIdWindow = 0;

    internal const int DwmwaCloaked = 14;

    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    internal delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime);
    internal delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal nint ExtraInfo;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint hwnd, ref Point point);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint newValue);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        nint moduleHandle,
        uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        nint hwnd,
        int attribute,
        out int value,
        int valueSize);

    internal static string ReadWindowTitle(nint hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 1);
        _ = GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    internal static bool TryGetClientBounds(nint hwnd, out IntRect bounds)
    {
        bounds = default;
        if (!GetClientRect(hwnd, out var nativeRect))
        {
            return false;
        }

        var origin = new Point { X = nativeRect.Left, Y = nativeRect.Top };
        if (!ClientToScreen(hwnd, ref origin))
        {
            return false;
        }

        bounds = ClientAreaCalculator.ToScreenRect(
            new ClientRect(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom),
            new IntPoint(origin.X, origin.Y));
        return !bounds.IsEmpty;
    }

    internal static bool IsCloaked(nint hwnd)
    {
        var result = DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }
}
