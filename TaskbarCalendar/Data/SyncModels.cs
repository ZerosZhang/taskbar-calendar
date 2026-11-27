using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TaskbarCalendar.Data;

/// <summary>
/// 同步用的完整事项，字段与远端 JSON 一一对应。
/// </summary>
public sealed class SyncItem
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string Note { get; set; } = "";

    public string? DueDate { get; set; }

    public int Kind { get; set; }

    /// <summary>
    /// 标签：0 无 / 1 重要 / 2 高亮 / 3 周期。合并了原优先级与重复。
    /// </summary>
    public int Tag { get; set; }

    public string? CompletedAt { get; set; }

    public string? DeletedAt { get; set; }

    /// <summary>
    /// 重复规则：weekly / monthly / yearly / custom；daily、workday 仅为旧数据保留。
    /// 仅 Tag 为周期时有意义，null 表示不重复。
    /// </summary>
    public string? RepeatRule { get; set; }

    /// <summary>
    /// 自定义重复的多个日期，逗号分隔的 yyyy-MM-dd；仅 Tag 为周期且 RepeatRule 为 custom 时有意义。
    /// </summary>
    public string? RepeatDates { get; set; }

    public string CreatedAt { get; set; } = "";

    public string UpdatedAt { get; set; } = "";

    /// <summary>
    /// 时光序事项类型：1 日程 / 2 重复 / 3 清单。只用于时光序缓存，不写入同步文件。
    /// </summary>
    [JsonIgnore]
    public int TodoType { get; set; } = 1;

    /// <summary>
    /// 时光序原始时间串 yyyyMMddHHmmss，回写完成状态时要原样带回。
    /// </summary>
    [JsonIgnore]
    public string? TodoTime { get; set; }

    /// <summary>
    /// 时光序重复类型：2 每周 / 7 任意多天；0 表示不重复。
    /// </summary>
    [JsonIgnore]
    public int RepeatType { get; set; }

    /// <summary>
    /// 重复起始时间 yyyyMMddHHmmss。
    /// </summary>
    [JsonIgnore]
    public string? StartTime { get; set; }

    /// <summary>
    /// 重复结束时间 yyyyMMddHHmmss。
    /// </summary>
    [JsonIgnore]
    public string? EndTime { get; set; }

    /// <summary>
    /// 时光序分类名（待办 / 项目 / 重要…），只用于本地缓存展示。
    /// </summary>
    [JsonIgnore]
    public string? ClassifyName { get; set; }
}
