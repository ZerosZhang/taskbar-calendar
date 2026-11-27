using System;
using System.Windows.Media;

namespace TaskbarCalendar.Views;

/// <summary>
/// 列表里的一行事项。
/// </summary>
public sealed class TodoRow
{
    private static readonly Brush ImportantBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x3A, 0x3A));

    private static readonly Brush HighlightBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B));

    private static readonly Brush PeriodicBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x3F, 0xE0));

    private static readonly Brush NoneBrush = new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00));


    public required string Id { get; init; }

    public required string Title { get; init; }

    public required int Kind { get; init; }

    public required int Tag { get; init; }

    /// <summary>
    /// 该行所属的日期；null 表示无日期。
    /// </summary>
    public DateOnly? DueDate { get; init; }

    /// <summary>
    /// 刚完成、仍在本次面板会话中显示以便撤销。
    /// </summary>
    public bool IsCompleted { get; init; }

    /// <summary>
    /// 只读行：勾选框和标签都不响应点击（时光序数据由同步服务维护）。
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    /// 用实心圆点代替勾选框：时光序的日程、重复类事项。
    /// </summary>
    public bool ShowDot { get; init; }

    /// <summary>
    /// 标签上的文字（重要 / 项目 / 重复）；空表示不显示标签。
    /// </summary>
    public string TagText { get; init; } = "";

    /// <summary>
    /// 备注有内容时，标题后面显示文档图标。
    /// </summary>
    public bool HasNote { get; init; }

    public string NoteText { get; init; } = "";

    /// <summary>
    /// 行内显示的时间文字；无日期为空。
    /// </summary>
    public string DueText => DueDate?.ToString("yyyy-MM-dd") ?? "";

    /// <summary>
    /// 逾期未完成的待办，面板里标红显示。
    /// </summary>
    public bool IsOverdue { get; init; }

    /// <summary>
    /// 标签颜色：重要（红）/ 高亮（绿）/ 周期（紫）/ 无。
    /// </summary>
    public Brush TagBrush => Tag switch
    {
        1 => ImportantBrush,
        2 => HighlightBrush,
        3 => PeriodicBrush,
        _ => NoneBrush
    };
}
