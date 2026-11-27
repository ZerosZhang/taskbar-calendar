using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 点击接管的实现方式。
/// </summary>
public enum ClockHitTestMode
{
    /// <summary>
    /// 全局低级鼠标钩子，只吞掉落在时钟矩形内的左键，视觉与悬停效果保持系统原样。
    /// </summary>
    MouseHook,

    /// <summary>
    /// 在时钟上方盖一个近乎全透明的窗口来接点击，不安装钩子；会失去时钟的悬停高亮。
    /// </summary>
    Overlay
}
