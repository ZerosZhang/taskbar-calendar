using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TaskbarCalendar.Interop;
using TaskbarCalendar.Services;

namespace TaskbarCalendar.Views;

/// <summary>
/// 覆盖在任务栏时钟上的透明命中窗口，仅用于 <see cref="ClockHitTestMode.Overlay"/> 模式。
/// 背景 alpha 设为 1，既能接收点击又不遮挡下面的时钟文字。
/// </summary>
public partial class ClockHitOverlayWindow : Window
{
    public event EventHandler? Clicked;

    public ClockHitOverlayWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 按物理像素把窗口贴到时钟矩形上。
    /// </summary>
    public void MoveTo(ClockBounds _bounds)
    {
        if (IsVisible is false)
        {
            Show();
        }

        IntPtr _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Width, _bounds.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void Root_MouseLeftButtonDown(object _sender, MouseButtonEventArgs _e)
    {
        Clicked?.Invoke(this, EventArgs.Empty);
    }
}
