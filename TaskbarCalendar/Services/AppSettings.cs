using System;

namespace TaskbarCalendar.Services;

/// <summary>
/// 用户设置，序列化到 <see cref="AppPaths.SettingsFile"/>。
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 点击拦截方式。
    /// </summary>
    public ClockHitTestMode HitTestMode { get; set; } = ClockHitTestMode.MouseHook;

    /// <summary>
    /// 手动指定的时光序 token；留空时自动读桌面端的登录信息。
    /// </summary>
    public string SgxTokenOverride { get; set; } = "";
}
