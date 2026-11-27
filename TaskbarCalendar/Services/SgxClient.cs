using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TaskbarCalendar.Data;

namespace TaskbarCalendar.Services;

/// <summary>
/// 时光序接口客户端：只做「拉取全部事项」这一件事。
/// 桌面端用 Authorization 头鉴权（网页端才是 token 头）。
/// </summary>
public sealed class SgxClient : IDisposable
{
    private const string BaseUrl = "https://api.weilaizhushou.com";

    private const string ViewPath = "/service/aggregation/view/recordviewload";

    // - 服务端限制：pageSize 最大 400
    private const int PageSize = 400;

    /// <summary>
    /// 时光序事项类型：清单（无日期，展示为备忘）。
    /// </summary>
    private const int TodoTypeChecklist = 3;

    /// <summary>
    /// 时光序事项类型：重复（面板里当备忘显示）。
    /// </summary>
    private const int TodoTypeRepeat = 2;

    /// <summary>
    /// 时光序事项类型：日程（面板里作可勾选的待办）。
    /// </summary>
    private const int TodoTypeSchedule = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient Http;

    private string? _defaultClassifyId;

    private readonly Dictionary<string, string> _classifyNames = [];

    public SgxClient()
    {
        Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 TaskbarCalendar");
    }

    /// <summary>
    /// 拉取全部事项（按分类分组返回，这里拍平去重）。
    /// </summary>
    public async Task<List<SyncItem>> FetchAllAsync(string _token, CancellationToken _cancellation)
    {
        Dictionary<string, SyncItem> _result = [];
        string _now = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        await LoadClassifiesAsync(_token, _cancellation);

        for (int _page = 1; _page <= 30; _page++)
        {
            object _payload = new
            {
                currentPage = _page,
                pageSize = PageSize,
                isAllInfo = true,
                keyword = "classify",
                offset = (_page - 1) * PageSize,
                theDateTime = _now,
                isFilterFinished = true,
                sortType = "TIME_AXIS"
            };

            using HttpRequestMessage _request = new(HttpMethod.Post, BaseUrl + ViewPath);
            _request.Headers.TryAddWithoutValidation("Authorization", _token);
            _request.Content = JsonContent.Create(_payload);

            using HttpResponseMessage _response = await Http.SendAsync(_request, _cancellation);
            if (_response.IsSuccessStatusCode is false)
            {
                throw new SgxException($"接口返回 {(int)_response.StatusCode}，token 可能已失效");
            }

            string _json = await _response.Content.ReadAsStringAsync(_cancellation);
            ViewResponse? _body = JsonSerializer.Deserialize<ViewResponse>(_json, JsonOptions);
            if (_body is null || _body.Code != 200)
            {
                throw new SgxException($"接口错误：{_body?.Message ?? "无法解析响应"}");
            }

            int _before = _result.Count;
            foreach (ViewGroup _group in _body.Data?.Rows ?? [])
            {
                foreach (SgxRecord _record in _group.ViewData?.Data?.Rows ?? [])
                {
                    SyncItem _item = Map(_record);
                    if (string.IsNullOrEmpty(_item.Id) is false)
                    {
                        _result[_item.Id] = _item;
                    }
                }
            }

            // - 没有新增说明翻到底了
            if (_result.Count == _before)
            {
                break;
            }
        }

        return [.. _result.Values];
    }

    /// <summary>
    /// 新建事项：有日期建日程（todoType=1），没日期建清单（todoType=3）。
    /// </summary>
    public async Task CreateAsync(
        string _token, string _raw_text, string _title, DateTime? _due, CancellationToken _cancellation)
    {
        string _classify = await ResolveClassifyIdAsync(_token, _cancellation);

        Dictionary<string, object?> _payload = new()
        {
            ["localId"] = (int)(DateTime.Now.Ticks % 100000),
            ["todoType"] = _due is null ? TodoTypeChecklist : 1,
            ["importance"] = 2,
            ["remark"] = "",
            ["shortTitle"] = _title,
            ["title"] = _raw_text,
            ["attachments"] = Array.Empty<object>(),
            ["todoClassifyId"] = _classify,
            ["address"] = "",
            ["longitude"] = "",
            ["latitude"] = "",
            ["sonDeleteList"] = Array.Empty<object>(),
            ["sonAddList"] = Array.Empty<object>(),
            ["sonUpdateList"] = Array.Empty<object>(),
            ["aheadType"] = new[] { new { offset = 0 } }
        };

        if (_due is not null)
        {
            _payload["intervalType"] = 1;
            _payload["todoTime"] = _due.Value.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        await PostAsync(_token, "/base/plan/record/add", _payload, _cancellation);
    }

    /// <summary>
    /// 新建事项需要一个分类 id，优先用「待办」分类，取到后缓存。
    /// </summary>
    private async Task<string> ResolveClassifyIdAsync(string _token, CancellationToken _cancellation)
    {
        if (string.IsNullOrEmpty(_defaultClassifyId) is false)
        {
            return _defaultClassifyId;
        }

        List<ClassifyItem> _list = await FetchClassifiesAsync(_token, _cancellation);
        ClassifyItem? _picked = _list.FirstOrDefault(_item => _item.IsFolder is false
                && (_item.Name?.Contains("待办", StringComparison.Ordinal) ?? false))
            ?? _list.FirstOrDefault(_item => _item.IsFolder is false);

        if (_picked?.Id is null)
        {
            throw new SgxException("没有可用的时光序分类");
        }

        _defaultClassifyId = _picked.Id;
        foreach (ClassifyItem _item in _list)
        {
            if (_item.Id is not null && _item.Name is not null)
            {
                _classifyNames[_item.Id] = _item.Name;
            }
        }

        return _defaultClassifyId;
    }

    private async Task<List<ClassifyItem>> FetchClassifiesAsync(string _token, CancellationToken _cancellation)
    {
        using HttpRequestMessage _request = new(HttpMethod.Post, BaseUrl + "/service/aggregation/todo/classify/v3/list");
        _request.Headers.TryAddWithoutValidation("Authorization", _token);
        _request.Content = JsonContent.Create(new { classifyVersion = "4", matterClassifyVersion = "4" });

        using HttpResponseMessage _response = await Http.SendAsync(_request, _cancellation);
        string _json = await _response.Content.ReadAsStringAsync(_cancellation);
        ClassifyResponse? _body = JsonSerializer.Deserialize<ClassifyResponse>(_json, JsonOptions);
        return _body?.Data?.List ?? [];
    }

    /// <summary>
    /// 取某条重复事项在区间内的实际实例日期，由时光序自己算，不用本地推算。
    /// </summary>
    public async Task<HashSet<DateOnly>> GetRepeatOccurrencesAsync(
        string _token, string _todo_id, DateOnly _from, DateOnly _to, CancellationToken _cancellation)
    {
        using HttpRequestMessage _request = new(HttpMethod.Post, BaseUrl + "/base/plan/view/repeatInfoByDateRange");
        _request.Headers.TryAddWithoutValidation("Authorization", _token);
        _request.Content = JsonContent.Create(new
        {
            todoId = _todo_id,
            startDay = _from.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            endDay = _to.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
        });

        using HttpResponseMessage _response = await Http.SendAsync(_request, _cancellation);
        string _json = await _response.Content.ReadAsStringAsync(_cancellation);
        RepeatRangeResponse? _body = JsonSerializer.Deserialize<RepeatRangeResponse>(_json, JsonOptions);

        HashSet<DateOnly> _dates = [];
        foreach (RepeatDay _day in _body?.Data?.Rows ?? [])
        {
            if (DateOnly.TryParseExact(_day.Day, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly _date))
            {
                _dates.Add(_date);
            }
        }

        return _dates;
    }

    /// <summary>
    /// 回写完成状态。清单走 checklist 接口，日程/重复走 checkin / repeatuncheck。
    /// </summary>
    public async Task SetFinishedAsync(string _token, SyncItem _item, bool _done, CancellationToken _cancellation)
    {
        int _finish_state = _done ? 1 : 0;
        string _path;
        object _payload;

        if (_item.TodoType == TodoTypeChecklist)
        {
            _path = "/base/plan/checklist/checkin/all";
            _payload = new { finishState = _finish_state, checklistId = _item.Id };
        }
        else
        {
            _path = _done ? "/base/plan/checkin" : "/base/plan/repeatuncheck";
            _payload = new
            {
                finishState = _finish_state,
                todoId = _item.Id,
                todoTime = _item.TodoTime ?? ""
            };
        }

        await PostAsync(_token, _path, _payload, _cancellation);
    }

    private async Task PostAsync(string _token, string _path, object _payload, CancellationToken _cancellation)
    {
        using HttpRequestMessage _request = new(HttpMethod.Post, BaseUrl + _path);
        _request.Headers.TryAddWithoutValidation("Authorization", _token);
        _request.Content = JsonContent.Create(_payload);

        using HttpResponseMessage _response = await Http.SendAsync(_request, _cancellation);
        string _json = await _response.Content.ReadAsStringAsync(_cancellation);

        if (_response.IsSuccessStatusCode is false)
        {
            throw new SgxException($"接口返回 {(int)_response.StatusCode}，token 可能已失效");
        }

        ResultResponse? _body = JsonSerializer.Deserialize<ResultResponse>(_json, JsonOptions);
        if (_body is null || _body.Code != 200)
        {
            throw new SgxException($"时光序返回：{_body?.Message ?? "无法解析响应"}");
        }
    }

    /// <summary>
    /// 时光序记录 → 本地缓存行。
    /// </summary>
    private SyncItem Map(SgxRecord _record)
    {
        string _updated = ToTimeText(_record.Updated) ?? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff");

        return new SyncItem
        {
            Id = _record.Id ?? "",
            Title = (_record.ShortTitle ?? "").Trim(),
            Note = _record.Remark ?? "",
            DueDate = ToDateText(_record.TodoTime),
            // - 只有日程是可勾选的待办；清单未完成时按备忘显示（圆点），重复一律当备忘
            Kind = _record.TodoType switch
            {
                TodoTypeRepeat => TodoKind.Memo,
                TodoTypeSchedule => IsFinished(_record) ? TodoKind.Done : TodoKind.Todo,
                _ => IsFinished(_record) ? TodoKind.Done : TodoKind.Memo
            },
            Tag = ResolveTag(_record),
            ClassifyName = ResolveClassifyName(_record),
            CompletedAt = IsFinished(_record) ? _updated : null,
            DeletedAt = _record.Deleted ? _updated : null,
            CreatedAt = ToTimeText(_record.Created) ?? _updated,
            UpdatedAt = _updated,
            TodoType = _record.TodoType,
            TodoTime = _record.TodoTime,
            RepeatType = _record.RepeatType,
            StartTime = _record.StartDatetime,
            EndTime = _record.EndDatetime
        };
    }

    /// <summary>
    /// 时光序的完成标记：普通事项放在 finishState，重复事项放在 finished。
    /// </summary>
    private static bool IsFinished(SgxRecord _record) => _record.FinishState > 0 || _record.Finished > 0;

    /// <summary>
    /// 颜色规则：重复事项一律「重复」（紫），其余的按所属分类——重要（红）、项目（绿）、待办（无）。
    /// </summary>
    private int ResolveTag(SgxRecord _record)
    {
        if (_record.TodoType == TodoTypeRepeat)
        {
            return TodoTag.Periodic;
        }

        string _classify = ResolveClassifyName(_record) ?? "";
        if (_classify.Contains("重要", StringComparison.Ordinal))
        {
            return TodoTag.Important;
        }

        return _classify.Contains("项目", StringComparison.Ordinal) ? TodoTag.Highlight : TodoTag.None;
    }

    private string? ResolveClassifyName(SgxRecord _record)
    {
        return _record.ClassifyId is not null
            && _classifyNames.TryGetValue(_record.ClassifyId, out string? _name)
            ? _name
            : null;
    }

    /// <summary>
    /// 分类 id → 名称，用于上色和展示。
    /// </summary>
    private async Task LoadClassifiesAsync(string _token, CancellationToken _cancellation)
    {
        List<ClassifyItem> _list = await FetchClassifiesAsync(_token, _cancellation);
        foreach (ClassifyItem _item in _list)
        {
            if (_item.Id is not null && _item.Name is not null)
            {
                _classifyNames[_item.Id] = _item.Name;
            }
        }
    }

    /// <summary>
    /// 时光序时间格式 yyyyMMddHHmmss → yyyy-MM-dd；空值或 0000 开头视为无日期。
    /// </summary>
    private static string? ToDateText(string? _value)
    {
        if (string.IsNullOrWhiteSpace(_value) || _value.StartsWith("0000", StringComparison.Ordinal) || _value.Length < 8)
        {
            return null;
        }

        return _value.Length >= 8
            ? $"{_value[..4]}-{_value.Substring(4, 2)}-{_value.Substring(6, 2)}"
            : null;
    }

    private static string? ToTimeText(long _epoch_milliseconds)
    {
        if (_epoch_milliseconds <= 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(_epoch_milliseconds).LocalDateTime
                .ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public void Dispose() => Http.Dispose();

    #region 响应模型

    private sealed class RepeatRangeResponse
    {
        [JsonPropertyName("data")]
        public RepeatRangeData? Data { get; set; }
    }

    private sealed class RepeatRangeData
    {
        [JsonPropertyName("rows")]
        public List<RepeatDay>? Rows { get; set; }
    }

    private sealed class RepeatDay
    {
        [JsonPropertyName("theDay")]
        public string? Day { get; set; }
    }

    private sealed class ClassifyResponse
    {
        [JsonPropertyName("data")]
        public ClassifyData? Data { get; set; }
    }

    private sealed class ClassifyData
    {
        [JsonPropertyName("list")]
        public List<ClassifyItem>? List { get; set; }
    }

    private sealed class ClassifyItem
    {
        [JsonPropertyName("classifyId")]
        public string? Id { get; set; }

        [JsonPropertyName("classifyName")]
        public string? Name { get; set; }

        [JsonPropertyName("isFolder")]
        public bool IsFolder { get; set; }
    }

    private sealed class ResultResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Message { get; set; }
    }

    private sealed class ViewResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("msg")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public ViewData? Data { get; set; }
    }

    private sealed class ViewData
    {
        [JsonPropertyName("rows")]
        public List<ViewGroup>? Rows { get; set; }
    }

    private sealed class ViewGroup
    {
        [JsonPropertyName("keyId")]
        public string? KeyId { get; set; }

        [JsonPropertyName("viewData")]
        public ViewGroupData? ViewData { get; set; }
    }

    private sealed class ViewGroupData
    {
        [JsonPropertyName("data")]
        public ViewGroupRows? Data { get; set; }
    }

    private sealed class ViewGroupRows
    {
        [JsonPropertyName("rows")]
        public List<SgxRecord>? Rows { get; set; }
    }

    private sealed class SgxRecord
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("shortTitle")]
        public string? ShortTitle { get; set; }

        [JsonPropertyName("remark")]
        public string? Remark { get; set; }

        [JsonPropertyName("todoTime")]
        public string? TodoTime { get; set; }

        [JsonPropertyName("finishState")]
        public int FinishState { get; set; }

        [JsonPropertyName("finished")]
        public int Finished { get; set; }

        [JsonPropertyName("importance")]
        public int Importance { get; set; }

        [JsonPropertyName("repeatType")]
        public int RepeatType { get; set; }

        [JsonPropertyName("deleted")]
        public bool Deleted { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("updated")]
        public long Updated { get; set; }

        [JsonPropertyName("todoType")]
        public int TodoType { get; set; }

        [JsonPropertyName("todoClassifyId")]
        public string? ClassifyId { get; set; }

        [JsonPropertyName("startDatetime")]
        public string? StartDatetime { get; set; }

        [JsonPropertyName("endDatetime")]
        public string? EndDatetime { get; set; }
    }

    #endregion
}

/// <summary>
/// 时光序接口异常：token 失效、返回码异常等，消息直接展示给用户。
/// </summary>
public sealed class SgxException(string _message) : Exception(_message);
