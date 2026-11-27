using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 时钟点击的拦截方式。所有实现都只影响本进程，停掉即恢复系统行为。
/// </summary>
public interface IClockHitWatcher : IDisposable
{
    /// <summary>
    /// 用户点击任务栏时钟时触发（此时系统原生弹窗已被拦下）。
    /// </summary>
    event EventHandler? ClockClicked;

    bool IsRunning { get; }

    void Start();

    void Stop();
}
