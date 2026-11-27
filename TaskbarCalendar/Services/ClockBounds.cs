using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 任务栏时钟的屏幕矩形，单位是物理像素。
/// </summary>
public readonly record struct ClockBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool Contains(int _x, int _y) => _x >= Left && _x < Right && _y >= Top && _y < Bottom;
}
