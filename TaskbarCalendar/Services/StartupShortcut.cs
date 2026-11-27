using System;
using System.IO;

namespace TaskbarCalendar.Services;

/// <summary>
/// 开机启动：在「启动」文件夹里放一个指向本程序的快捷方式，不写注册表。
/// </summary>
public static class StartupShortcut
{
    private const string ShortcutName = "任务栏日历.lnk";

    /// <summary>
    /// 静默启动参数：跳过管理窗口，只常驻接管时钟。
    /// </summary>
    public const string SilentArgument = "--silent";

    /// <summary>
    /// 当前用户的启动文件夹，例如 %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup。
    /// </summary>
    public static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        ShortcutName);

    public static bool IsEnabled => File.Exists(ShortcutPath);

    /// <summary>
    /// 创建快捷方式；失败时抛出异常，由调用方提示。
    /// </summary>
    public static void Enable()
    {
        string _target = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "TaskbarCalendar.exe");

        Type? _shell_type = Type.GetTypeFromProgID("WScript.Shell");
        if (_shell_type is null)
        {
            throw new InvalidOperationException("系统缺少 WScript.Shell，无法创建快捷方式");
        }

        dynamic _shell = Activator.CreateInstance(_shell_type)!;
        dynamic _shortcut = _shell.CreateShortcut(ShortcutPath);
        _shortcut.TargetPath = _target;
        _shortcut.WorkingDirectory = Path.GetDirectoryName(_target) ?? AppContext.BaseDirectory;
        _shortcut.IconLocation = _target;
        _shortcut.Description = "任务栏日历";
        // - 开机启动时不弹窗口，只保留任务栏时钟接管
        _shortcut.Arguments = SilentArgument;
        _shortcut.Save();
    }

    public static void Disable()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }
}
