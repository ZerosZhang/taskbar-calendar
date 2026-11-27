using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using TaskbarCalendar.Interop;

namespace TaskbarCalendar.Services;

/// <summary>
/// 全局低级鼠标钩子：识别落在任务栏时钟上的左键点击，吞掉它并通知上层，
/// 这样 Explorer 不会弹出通知中心 / 日历，改由本程序接管。
/// 钩子不注入任何进程，卸载后系统行为完全恢复。
/// </summary>
public sealed class MouseHookClockHitWatcher : IClockHitWatcher, IOutsideClickSource
{
    private readonly ClockBoundsMonitor ClockBoundsMonitor;
    private readonly NativeMethods.HookProc MouseProcDelegate;
    private readonly Dispatcher Dispatcher;
    private readonly DispatcherTimer HeartbeatTimer;

    private IntPtr HookHandle = IntPtr.Zero;
    private bool IsLeftDownSwallowed = false;
    private bool IsDisposed = false;

    public event EventHandler? ClockClicked;

    /// <summary>
    /// 任意左键按下（物理像素坐标），由上层决定是否在面板外。
    /// </summary>
    public event EventHandler<MouseDownEventArgs>? MouseDownAt;

    public MouseHookClockHitWatcher(ClockBoundsMonitor _clock_bounds_monitor)
    {
        ClockBoundsMonitor = _clock_bounds_monitor;
        Dispatcher = Dispatcher.CurrentDispatcher;
        // ! 必须持有委托实例，否则会被 GC 回收导致钩子回调崩溃
        MouseProcDelegate = MouseProc;

        // - 系统在回调超时后会静默摘除钩子且不给通知，定期重装做兜底
        HeartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        HeartbeatTimer.Tick += HeartbeatTimer_Tick;
    }

    public bool IsRunning => HookHandle != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        InstallHook();
        HeartbeatTimer.Start();
    }

    public void Stop()
    {
        HeartbeatTimer.Stop();
        RemoveHook();
    }

    private void InstallHook(bool _log = true)
    {
        HookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, MouseProcDelegate, NativeMethods.GetModuleHandle(null), 0);
        if (HookHandle == IntPtr.Zero)
        {
            AppLog.Write($"安装鼠标钩子失败，错误码 {Marshal.GetLastWin32Error()}");
            return;
        }

        if (_log)
        {
            AppLog.Write("鼠标钩子已安装");
        }
    }

    private void RemoveHook(bool _log = true)
    {
        if (HookHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(HookHandle);
        HookHandle = IntPtr.Zero;
        IsLeftDownSwallowed = false;
        if (_log)
        {
            AppLog.Write("鼠标钩子已卸载");
        }
    }

    private void HeartbeatTimer_Tick(object? _sender, EventArgs _args)
    {
        // - 重装一次，防止系统在极端情况下静默摘钩
        RemoveHook(false);
        InstallHook(false);
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        Stop();
        IsDisposed = true;
    }

    private IntPtr MouseProc(int _n_code, IntPtr _w_param, IntPtr _l_param)
    {
        try
        {
            if (_n_code >= 0)
            {
                int _message = _w_param.ToInt32();
                if (_message == NativeMethods.WM_LBUTTONDOWN)
                {
                    NativeMethods.MSLLHOOKSTRUCT _info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(_l_param);
                    if (ClockBoundsMonitor.TryGet(out ClockBounds _bounds)
                        && _bounds.Contains(_info.Pt.X, _info.Pt.Y)
                        && IsPointOnOwnWindow(_info.Pt) is false)
                    {
                        // ! 吞掉按下，同时记下状态以便吞掉随后的抬起
                        IsLeftDownSwallowed = true;
                        AppLog.Write($"拦截时钟点击 {_info.Pt.X},{_info.Pt.Y}");

                        // ! 回调必须尽快返回：超过系统阈值（约 300ms）钩子会被静默摘除，
                        // ! 所以真正的开窗逻辑丢回 UI 线程异步执行
                        Dispatcher.BeginInvoke(() => ClockClicked?.Invoke(this, EventArgs.Empty));
                        return new IntPtr(1);
                    }

                    // - 面板打开期间上层会订阅此事件，立即判断是否点在面板外
                    EventHandler<MouseDownEventArgs>? _down_handler = MouseDownAt;
                    if (_down_handler is not null)
                    {
                        int _x = _info.Pt.X;
                        int _y = _info.Pt.Y;
                        Dispatcher.BeginInvoke(() => _down_handler(this, new MouseDownEventArgs(_x, _y)));
                    }
                }
                else if (_message == NativeMethods.WM_LBUTTONUP && IsLeftDownSwallowed)
                {
                    IsLeftDownSwallowed = false;
                    return new IntPtr(1);
                }
            }
        }
        catch (Exception _ex)
        {
            // ? 钩子回调里抛异常会被系统摘掉钩子，这里只记录不抛出
            AppLog.Write($"鼠标钩子回调异常：{_ex.Message}");
        }

        return NativeMethods.CallNextHookEx(HookHandle, _n_code, _w_param, _l_param);
    }

    private static bool IsPointOnOwnWindow(NativeMethods.POINT _point)
    {
        IntPtr _window = NativeMethods.WindowFromPoint(_point);
        if (_window == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(_window, out uint _process_id);
        return _process_id == (uint)Environment.ProcessId;
    }
}
