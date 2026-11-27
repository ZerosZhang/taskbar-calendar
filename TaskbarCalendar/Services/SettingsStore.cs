using System;
using System.IO;
using System.Text.Json;

namespace TaskbarCalendar.Services;

/// <summary>
/// 设置的读写。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile) is false)
            {
                return new AppSettings();
            }

            string _json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(_json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception _ex) when (_ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write($"读取设置失败：{_ex.Message}");
            return new AppSettings();
        }
    }
}
