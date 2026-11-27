using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace TaskbarCalendar.Services;

/// <summary>
/// 从时光序桌面端（Electron）的 Local Storage 里读出登录 token。
/// 桌面端 token 有效期约 30 天，比网页端长，打开一次桌面端就会刷新写入。
/// </summary>
public static class SgxTokenReader
{
    private static readonly Regex TokenPattern = new(
        "\"token\"\\s*:\\s*\"(eyJ[A-Za-z0-9_\\-\\.]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex ExpiresPattern = new(
        "\"expires\"\\s*:\\s*(\\d{10,})",
        RegexOptions.Compiled);

    public readonly record struct TokenInfo(string Token, DateTime? Expires, string Source);

    /// <summary>
    /// 桌面端数据目录：%APPDATA%\shiguangxu\Local Storage\leveldb。
    /// </summary>
    public static string LevelDbDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "shiguangxu", "Local Storage", "leveldb");

    /// <summary>
    /// 取最近写入的 token；读不到返回 null。
    /// </summary>
    public static TokenInfo? ReadLatest()
    {
        string _directory = LevelDbDirectory;
        if (Directory.Exists(_directory) is false)
        {
            return null;
        }

        List<FileInfo> _files = [.. new DirectoryInfo(_directory).GetFiles("*")
            .Where(_file => _file.Extension is ".log" or ".ldb")];
        _files.Sort((_a, _b) => _b.LastWriteTimeUtc.CompareTo(_a.LastWriteTimeUtc));

        foreach (FileInfo _file in _files)
        {
            string? _text = TryReadText(_file.FullName);
            if (_text is null)
            {
                continue;
            }

            // - 一个文件里可能有多条历史记录，取最后一条
            Match? _match = TokenPattern.Matches(_text).LastOrDefault();
            if (_match is null || _match.Success is false)
            {
                continue;
            }

            DateTime? _expires = null;
            Match _expires_match = ExpiresPattern.Match(_text, _match.Index + _match.Length);
            if (_expires_match.Success && long.TryParse(_expires_match.Groups[1].Value, out long _ms))
            {
                try
                {
                    _expires = DateTimeOffset.FromUnixTimeMilliseconds(_ms).LocalDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    _expires = null;
                }
            }

            return new TokenInfo(_match.Groups[1].Value, _expires, _file.Name);
        }

        return null;
    }

    private static string? TryReadText(string _path)
    {
        // - 时光序正在运行时 leveldb 会短时独占读写，这里重试几次
        for (int _attempt = 0; _attempt < 3; _attempt++)
        {
            try
            {
                using FileStream _stream = new(
                    _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using MemoryStream _buffer = new();
                _stream.CopyTo(_buffer);

                // - leveldb 是追加写的二进制文件，按字节扫描即可，非法序列用替换字符兜住
                return Encoding.UTF8.GetString(_buffer.ToArray());
            }
            catch (Exception _ex) when (_ex is IOException or UnauthorizedAccessException)
            {
                if (_attempt == 2)
                {
                    AppLog.Write($"读取时光序 token 失败：{_ex.Message}");
                    return null;
                }

                Thread.Sleep(200);
            }
        }

        return null;
    }
}
