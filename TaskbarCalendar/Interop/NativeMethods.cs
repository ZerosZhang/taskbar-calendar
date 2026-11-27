using System;
using System.Runtime.InteropServices;

namespace TaskbarCalendar.Interop;

/// <summary>
/// 本框架用到的 Win32 API 封装。
/// </summary>
internal static class NativeMethods
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    // - 显示器 DPI 类型：0 = MDT_EFFECTIVE_DPI
    public const int MDT_EFFECTIVE_DPI = 0;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public delegate IntPtr HookProc(int _n_code, IntPtr _w_param, IntPtr _l_param);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int _id_hook, HookProc _lp_fn, IntPtr _h_mod, uint _dw_thread_id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr _hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr _hhk, int _n_code, IntPtr _w_param, IntPtr _l_param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? _lp_module_name);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr _hwnd, IntPtr _h_wnd_insert_after, int _x, int _y, int _cx, int _cy, uint _u_flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr _hwnd, out RECT _lp_rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? _lp_class_name, string? _lp_window_name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr _h_wnd_parent, IntPtr _h_wnd_child_after, string? _lp_class_name, string? _lp_window_name);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT _point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT _lp_point);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT _point, uint _dw_flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr _hmonitor, int _dpi_type, out uint _dpi_x, out uint _dpi_y);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr _hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr _hwnd, out uint _lpdw_process_id);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint _id_attach, uint _id_attach_to, [MarshalAs(UnmanagedType.Bool)] bool _f_attach);

    /// <summary>
    /// 在全局鼠标钩子里显示窗口时，Windows 可能拒绝把前台交给我们，这里做一次线程输入附加兜底。
    /// </summary>
    public static void ForceForeground(IntPtr _hwnd)
    {
        IntPtr _foreground = GetForegroundWindow();
        if (_foreground == _hwnd)
        {
            return;
        }

        uint _foreground_thread = GetWindowThreadProcessId(_foreground, out uint _);
        uint _current_thread = GetCurrentThreadId();
        bool _attached = _foreground_thread != _current_thread
                         && _foreground_thread != 0
                         && AttachThreadInput(_current_thread, _foreground_thread, true);
        try
        {
            SetForegroundWindow(_hwnd);
        }
        finally
        {
            if (_attached)
            {
                AttachThreadInput(_current_thread, _foreground_thread, false);
            }
        }
    }
}
