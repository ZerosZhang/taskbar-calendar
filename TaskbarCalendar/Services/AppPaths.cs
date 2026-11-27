using System;
using System.IO;

namespace TaskbarCalendar.Services;

/// <summary>
/// 程序数据目录：优先用软件目录下的 data 文件夹（便携模式），不可写时退回用户目录。
/// </summary>
public static class AppPaths
{
    private static readonly string LegacyDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarCalendar");

    public static string DataDirectory { get; } = ResolveDataDirectory();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string DatabaseFile => Path.Combine(DataDirectory, "tasks.db");

    /// <summary>
    /// 把旧版本放在 %LOCALAPPDATA% 的数据搬到当前目录（只在目标不存在时复制）。
    /// </summary>
    public static void MigrateFromLegacyDirectory()
    {
        if (string.Equals(DataDirectory, LegacyDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string[] _file_names = ["tasks.db", "tasks.db-wal", "tasks.db-shm", "settings.json"];
        int _copied = 0;

        foreach (string _file_name in _file_names)
        {
            try
            {
                string _source = Path.Combine(LegacyDirectory, _file_name);
                string _target = Path.Combine(DataDirectory, _file_name);
                if (File.Exists(_source) && File.Exists(_target) is false)
                {
                    File.Copy(_source, _target);
                    _copied++;
                }
            }
            catch (Exception _ex) when (_ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write($"迁移旧数据 {_file_name} 失败：{_ex.Message}");
            }
        }

        if (_copied > 0)
        {
            AppLog.Write($"已从旧目录迁移 {_copied} 个文件到 {DataDirectory}");
        }
    }

    private static string ResolveDataDirectory()
    {
        string _portable = Path.Combine(AppContext.BaseDirectory, "data");
        if (IsWritable(_portable))
        {
            return _portable;
        }

        Directory.CreateDirectory(LegacyDirectory);
        return LegacyDirectory;
    }

    private static bool IsWritable(string _directory)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            string _probe = Path.Combine(_directory, ".write-test");
            File.WriteAllText(_probe, "1");
            File.Delete(_probe);
            return true;
        }
        catch (Exception _ex) when (_ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
