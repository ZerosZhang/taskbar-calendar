using System;
using System.IO;

namespace TaskbarCalendar.Services;

/// <summary>
/// 极简文件日志，便于排查钩子和窗口定位问题。
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "app.log");

    public static void Write(string _message)
    {
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                FileInfo _info = new(FilePath);
                if (_info.Exists && _info.Length > 1024 * 1024)
                {
                    _info.Delete();
                }

                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {_message}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
