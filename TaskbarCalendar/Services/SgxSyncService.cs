using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using TaskbarCalendar.Data;

namespace TaskbarCalendar.Services;

/// <summary>
/// 时光序 → 本地缓存的单向同步：拉全部事项，覆盖写进 sgx_item 表。
/// token 从时光序桌面端的 Local Storage 读取，桌面端登录一次可用约 30 天。
/// </summary>
public sealed class SgxSyncService : IDisposable
{
    private const int SyncIntervalMinutes = 10;

    private readonly AppSettings Settings;

    private readonly SgxDatabase Database;

    private readonly SgxClient Client = new();

    private readonly DispatcherTimer Timer;

    private CancellationTokenSource? Current;

    /// <summary>
    /// 同步状态文本变化，供界面展示。
    /// </summary>
    public event EventHandler<string>? StateChanged;

    public DateTime? LastSyncAt { get; private set; }

    /// <summary>
    /// 最近一次同步后的总条数。
    /// </summary>
    public int LastCount { get; private set; }

    public SgxSyncService(AppSettings _settings, SgxDatabase _database)
    {
        Settings = _settings;
        Database = _database;

        Timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(SyncIntervalMinutes)
        };
        Timer.Tick += Timer_Tick;
    }

    /// <summary>
    /// 启动时就同步一次，之后按间隔轮询。
    /// </summary>
    public void Start()
    {
        Timer.Start();
        _ = SyncNowAsync();
    }

    public async Task<(bool Ok, string Message)> SyncNowAsync()
    {
        CancellationTokenSource _source = new();
        CancellationTokenSource? _previous = Interlocked.Exchange(ref Current, _source);
        _previous?.Cancel();

        try
        {
            string? _token = ResolveToken();
            if (string.IsNullOrEmpty(_token))
            {
                return Fail("未找到时光序登录信息，请先登录时光序桌面端");
            }

            List<SyncItem> _items = await Client.FetchAllAsync(_token, _source.Token);
            Database.ApplyItems(_items, NowText());

            LastSyncAt = DateTime.Now;
            LastCount = Database.Count();
            string _message = $"时光序同步完成，共 {LastCount} 条";
            Report(_message);
            return (true, _message);
        }
        catch (OperationCanceledException)
        {
            return (false, "同步已取消");
        }
        catch (SgxException _ex)
        {
            return Fail(_ex.Message);
        }
        catch (Exception _ex)
        {
            return Fail($"时光序同步失败：{_ex.Message}");
        }
        finally
        {
            Interlocked.CompareExchange(ref Current, null, _source);
            _source.Dispose();
        }
    }

    /// <summary>
    /// 把一条事项的完成状态回写到时光序。成功返回 null，失败返回错误说明。
    /// </summary>
    public async Task<string?> SetFinishedAsync(string _id, bool _done)
    {
        SyncItem? _item = Database.GetItem(_id);
        if (_item is null)
        {
            return "本地缓存里找不到这条事项";
        }

        string? _token = ResolveToken();
        if (string.IsNullOrEmpty(_token))
        {
            return "未找到时光序登录信息";
        }

        try
        {
            using CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(20));
            await Client.SetFinishedAsync(_token, _item, _done, _timeout.Token);
            AppLog.Write($"时光序回写成功：{_item.Title} → {(_done ? "完成" : "未完成")}");
            return null;
        }
        catch (Exception _ex)
        {
            AppLog.Write($"时光序回写失败：{_item.Title}：{_ex.Message}");
            return $"回写时光序失败：{_ex.Message}";
        }
    }

    /// <summary>
    /// 在时光序新建一条事项，成功后立刻拉一次保证列表最新。
    /// </summary>
    public async Task<string?> CreateAsync(string _text)
    {
        string _raw = _text.Trim();
        if (_raw.Length == 0)
        {
            return "内容为空";
        }

        QuickInputResult _parsed = QuickInputParser.Parse(_raw, DateOnly.FromDateTime(DateTime.Today));
        if (_parsed.Title.Length == 0)
        {
            return "只写了时间，没有事项内容";
        }

        string? _token = ResolveToken();
        if (string.IsNullOrEmpty(_token))
        {
            return "未找到时光序登录信息";
        }

        DateTime? _due = null;
        if (_parsed.Date is not null)
        {
            _due = _parsed.Date.Value.ToDateTime(_parsed.Time ?? TimeOnly.MinValue);
        }

        try
        {
            using CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(20));
            await Client.CreateAsync(_token, _raw, _parsed.Title, _due, _timeout.Token);
            AppLog.Write($"时光序新建成功：{_parsed.Title}（{_due?.ToString("yyyy-MM-dd HH:mm") ?? "无日期"}）");
            await SyncNowAsync();
            return null;
        }
        catch (Exception _ex)
        {
            AppLog.Write($"时光序新建失败：{_raw}：{_ex.Message}");
            return $"新建失败：{_ex.Message}";
        }
    }

    /// <summary>
    /// 取某条重复事项在区间内的实例日期；失败时返回空集合。
    /// </summary>
    public async Task<HashSet<DateOnly>> GetOccurrencesAsync(SyncItem _item, DateOnly _from, DateOnly _to)
    {
        string? _token = ResolveToken();
        if (string.IsNullOrEmpty(_token) || string.IsNullOrEmpty(_item.Id))
        {
            return [];
        }

        using CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(15));
        return await Client.GetRepeatOccurrencesAsync(_token, _item.Id, _from, _to, _timeout.Token);
    }

    private string? ResolveToken()
    {
        if (string.IsNullOrWhiteSpace(Settings.SgxTokenOverride) is false)
        {
            return Settings.SgxTokenOverride.Trim();
        }

        return SgxTokenReader.ReadLatest()?.Token;
    }

    public void Dispose()
    {
        Timer.Stop();
        Timer.Tick -= Timer_Tick;
        Current?.Cancel();
        Client.Dispose();
    }

    private async void Timer_Tick(object? _sender, EventArgs _e)
    {
        await SyncNowAsync();
    }

    private (bool Ok, string Message) Fail(string _message)
    {
        Report(_message);
        return (false, _message);
    }

    private void Report(string _message)
    {
        AppLog.Write($"时光序：{_message}");
        StateChanged?.Invoke(this, _message);
    }

    private static string NowText() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff");
}
