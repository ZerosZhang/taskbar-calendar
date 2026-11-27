using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 能上报全局左键按下的拦截器（用于面板点外面立即关闭）。
/// 覆盖窗口方案没有全局鼠标事件，不实现该接口，退回轮询关闭。
/// </summary>
public interface IOutsideClickSource
{
    event EventHandler<MouseDownEventArgs>? MouseDownAt;
}

/// <summary>
/// 全局左键按下，坐标是物理屏幕像素。
/// </summary>
public sealed class MouseDownEventArgs : EventArgs
{
    public MouseDownEventArgs(int _x, int _y)
    {
        X = _x;
        Y = _y;
    }

    public int X { get; }

    public int Y { get; }
}
