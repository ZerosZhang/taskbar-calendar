using System;

namespace TaskbarCalendar.Data;

/// <summary>
/// 一条事项的原始数据，只包含列表需要的字段。
/// </summary>
public readonly record struct TodoItemRecord(
    string Id, string Title, int Kind, int Tag, string Note, int TodoType = 0);

/// <summary>
/// 事项状态：备忘（默认）、待办、已完成。
/// </summary>
public static class TodoKind
{
    public const int Memo = 0;

    public const int Todo = 1;

    public const int Done = 2;
}

/// <summary>
/// 事项标签：合并了原「优先级」与「重复」，一条事项同时只属于一种。
/// </summary>
public static class TodoTag
{
    public const int None = 0;

    /// <summary>
    /// 原高优先级（含中优先级）。
    /// </summary>
    public const int Important = 1;

    /// <summary>
    /// 原低优先级。
    /// </summary>
    public const int Highlight = 2;

    /// <summary>
    /// 原重复事项；只有该标签下 repeat_rule / repeat_dates 才有效。
    /// </summary>
    public const int Periodic = 3;

    public static string ToText(int _tag) => _tag switch
    {
        Important => "重要",
        Highlight => "高亮",
        Periodic => "周期",
        _ => "无"
    };
}
