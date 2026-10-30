using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;

namespace Tunly.Services;

/// <summary>
/// Notification-area icon built directly on <c>Shell_NotifyIcon</c>. Owns one
/// hidden top-level window that receives the tray messages on the UI thread.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    const uint WM_APP = 0x8000;
    const uint WM_NULL = 0x0000;

    const uint WM_LBUTTONUP = 0x0202;
    const uint WM_LBUTTONDBLCLK = 0x0203;
    const uint WM_RBUTTONUP = 0x0205;

    const uint NIM_ADD = 0x0000;
    const uint NIM_MODIFY = 0x0001;
    const uint NIM_DELETE = 0x0002;

    const uint NIF_MESSAGE = 0x0001;
    const uint NIF_ICON = 0x0002;
    const uint NIF_TIP = 0x0004;

    const uint IMAGE_ICON = 1;
    const uint LR_LOADFROMFILE = 0x0010;
    const int SM_CXSMICON = 49;
    const int SM_CYSMICON = 50;

    const uint TPM_RIGHTBUTTON = 0x0002;
    const uint TPM_NONOTIFY = 0x0080;
    const uint TPM_RETURNCMD = 0x0100;

    const uint MF_BYPOSITION = 0x0400;
    const uint MF_STRING = 0x0000;
    const uint MF_SEPARATOR = 0x0800;

    const int MenuOpenId = 1;
    const int MenuExitId = 2;

    const string ClassName = "TunlyTrayHost";

    static readonly WNDPROC WndProcHandler = WndProc;
    static readonly ConcurrentDictionary<nint, TrayIconService> Live = new();

    /// <summary>The user wants the window back (left click, double click, or menu).</summary>
    public event Action? ActivateRequested;

    /// <summary>The user chose "Exit" in the tray menu.</summary>
    public event Action? ExitRequested;

    readonly uint _id = 1;
    nint _icon;
    bool _added;
    bool _disposed;
    NOTIFYICONDATA _data;

    TrayIconService(nint hWnd) => _data.hWnd = hWnd;

    /// <summary>Creates the tray icon, or returns null when the shell refused it.</summary>
    public static TrayIconService? Create(string tooltip, string? iconFile, string? executablePath)
    {
        if (!RegisterWindowClass()) return null;

        var hWnd = CreateWindowExW(0, ClassName, ClassName, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (hWnd == 0) return null;

        var tray = new TrayIconService(hWnd);
        Live[hWnd] = tray;

        tray._icon = LoadSmallIcon(iconFile, executablePath);
        tray._data = new NOTIFYICONDATA
        {
            hWnd = hWnd,
            uID = tray._id,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_APP,
            hIcon = tray._icon,
            szTip = tooltip.Length > 127 ? tooltip[..127] : tooltip
        };

        tray._added = Shell_NotifyIconW(NIM_ADD, ref tray._data);
        if (!tray._added)
        {
            tray.Dispose();
            return null;
        }

        return tray;
    }

    public bool IsVisible => _added && !_disposed;
    nint Handle => _data.hWnd;

    /// <summary>Sets whether the icon shows in the notification area.</summary>
    public void SetVisible(bool visible)
    {
        if (_disposed || visible == _added) return;

        if (visible)
            _added = Shell_NotifyIconW(NIM_ADD, ref _data);
        else if (Shell_NotifyIconW(NIM_DELETE, ref _data))
            _added = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_data.hWnd != 0)
        {
            if (_added) Shell_NotifyIconW(NIM_DELETE, ref _data);
            DestroyWindow(_data.hWnd);
            Live.TryRemove(_data.hWnd, out _);
        }

        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    // ---------------- Window plumbing ----------------

    static nint WndProc(nint hWnd, uint message, nint wParam, nint lParam)
    {
        if (message == WM_APP && Live.TryGetValue(hWnd, out var instance))
        {
            var tray = instance;
            switch (lParam & 0xFFFF)
            {
                case (nint)WM_LBUTTONUP:
                case (nint)WM_LBUTTONDBLCLK:
                    tray.ActivateRequested?.Invoke();
                    return 0;

                case (nint)WM_RBUTTONUP:
                    tray.ShowContextMenu();
                    return 0;
            }

            return DefWindowProcW(hWnd, message, wParam, lParam);
        }

        return DefWindowProcW(hWnd, message, wParam, lParam);
    }

    void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;

        InsertMenuW(menu, 0, MF_BYPOSITION | MF_STRING, MenuOpenId, "&Open Tunly");
        InsertMenuW(menu, 1, MF_BYPOSITION | MF_SEPARATOR, 0, null);
        InsertMenuW(menu, 2, MF_BYPOSITION | MF_STRING, MenuExitId, "E&xit Tunly");

        SetForegroundWindow(Handle);
        GetCursorPos(out var point);
        var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, point.X, point.Y, Handle, 0);
        DestroyMenu(menu);
        PostMessageW(Handle, WM_NULL, 0, 0);

        switch (command)
        {
            case MenuOpenId:
                ActivateRequested?.Invoke();
                break;
            case MenuExitId:
                ExitRequested?.Invoke();
                break;
        }
    }

    static bool _classRegistered;

    static bool RegisterWindowClass()
    {
        if (_classRegistered) return true;

        var ex = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcHandler),
            hInstance = GetModuleHandleW(null),
            lpszClassName = ClassName
        };

        if (RegisterClassExW(ref ex) == 0)
        {
            if (GetLastError() != 1410 /* ERROR_CLASS_ALREADY_EXISTS */) return false;
        }

        // Keep the delegate alive for the lifetime of the process.
        GC.KeepAlive(WndProcHandler);
        _classRegistered = true;
        return true;
    }

    static nint LoadSmallIcon(string? iconFile, string? executablePath)
    {
        if (!string.IsNullOrWhiteSpace(iconFile) && File.Exists(iconFile))
        {
            var fromFile = LoadImageW(
                0, iconFile, IMAGE_ICON,
                GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON),
                LR_LOADFROMFILE);
            if (fromFile != 0) return fromFile;
        }

        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            // Index -1 counts, 0 is the first (the app icon) — small variant for the tray.
            if (ExtractIconExW(executablePath, 0, out _, out var small, 1) > 0)
                return small;
        }

        return 0;
    }

    // ---------------- Interop ----------------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    delegate nint WNDPROC(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll")]
    static extern nint DefWindowProcW(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool InsertMenuW(nint menu, uint position, uint flags, nint id, string? caption);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int TrackPopupMenuEx(
        nint menu, uint flags, int x, int y, nint hWnd, nint parameters);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PostMessageW(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern nint LoadImageW(
        nint instance, string name, uint type,
        int desiredWidth, int desiredHeight, uint load);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern uint ExtractIconExW(
        string file, int index, out nint largeIcon, out nint smallIcon, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(nint icon);

    [DllImport("kernel32.dll")]
    static extern uint GetLastError();
}
