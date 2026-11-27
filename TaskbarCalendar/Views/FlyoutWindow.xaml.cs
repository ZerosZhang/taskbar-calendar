using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TaskbarCalendar.Data;
using TaskbarCalendar.Interop;
using TaskbarCalendar.Services;

namespace TaskbarCalendar.Views;

/// <summary>
/// 接管任务栏时钟点击后弹出的轻量面板：看、勾、加。
/// </summary>
public partial class FlyoutWindow : Window
{
    // - 刚弹出时系统可能短暂把前台还给上一个窗口，这段时间内不做自动关闭判断
    private const int GracePeriodMilliseconds = 600;

    private readonly DispatcherTimer OutsideCheckTimer;
    private readonly DispatcherTimer ActivationRetryTimer;
    private readonly SgxDatabase Database;

    private readonly SgxSyncService? SyncService;

    private readonly DispatcherTimer MessageTimer;

    private IntPtr WindowHandle = IntPtr.Zero;
    private DateTime ShownAt = DateTime.MinValue;
    private int ForegroundMissCount = 0;
    private int ActivationRetryCount = 0;
    private bool IsCloseRequested = false;
    private DateOnly SelectedDate;
    private DateOnly DisplayMonth;

    /// <summary>
    /// 重复实例日期缓存：键为「事项 id | 年月」，值是该月内实际发生的日期（只用于显示，不落库）。
    /// </summary>
    private readonly Dictionary<string, HashSet<DateOnly>> RepeatOccurrences = [];

    private readonly HashSet<string> RepeatLoading = [];

    /// <summary>
    /// 是否允许失焦/点外部时自动关闭。界面预览模式下关闭该行为，方便单独观察界面。
    /// </summary>
    public bool AutoCloseEnabled { get; set; } = true;

    /// <summary>
    /// 点击面板里的「管理」时触发。
    /// </summary>
    public event EventHandler? SettingsRequested;

    public FlyoutWindow(SgxDatabase _database, SgxSyncService? _sync_service = null)
    {
        InitializeComponent();

        Database = _database;
        SyncService = _sync_service;
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);
        DisplayMonth = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);

        CalendarBoard.DateSelected += CalendarBoard_DateSelected;

        Closed += FlyoutWindow_Closed;

        OutsideCheckTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        OutsideCheckTimer.Tick += OutsideCheckTimer_Tick;

        // - 前台被系统拒收时重试几次，避免面板立刻被判定失焦
        ActivationRetryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        ActivationRetryTimer.Tick += ActivationRetryTimer_Tick;

        // - 回写失败等提示只在标题行短暂显示，随后恢复日期文字
        MessageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        MessageTimer.Tick += (_, _) =>
        {
            MessageTimer.Stop();
            SelectedDayText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x5F, 0x63, 0x68));
            UpdateSelectedDayText();
        };

        LoadContent();
    }

    #region 显示与定位

    /// <summary>
    /// 刷新内容并在时钟上方弹出。
    /// </summary>
    public void ShowNearClock(ClockBounds _bounds)
    {
        DateTime _now = DateTime.Now;
        TimeText.Text = CalendarTextProvider.GetTimeLine(_now);
        DateText.Text = CalendarTextProvider.GetDateLine(_now);
        ShownAt = _now;

        // - 每次打开回到今天，避免停留在上次浏览的日期
        SelectedDate = DateOnly.FromDateTime(_now);
        DisplayMonth = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);
        LoadContent();

        // ! 先按显示器的物理/逻辑比例算出 DIP 位置，再显示，避免"先出现在默认位置再跳过去"
        PositionNearClock(_bounds);

        Show();

        WindowHandle = new WindowInteropHelper(this).Handle;
        // - 混合 DPI 下换算可能有偏差，这里做一次不可见的兜底校正
        MoveNearClock(_bounds);
        NativeMethods.ForceForeground(WindowHandle);
        Activate();
        Opacity = 1;
        if (GetForegroundWindowSafe() != WindowHandle)
        {
            ActivationRetryCount = 0;
            ActivationRetryTimer.Start();
        }

        // ? 展示过程中若已被判定关闭，不再启动后续检测
        if (IsCloseRequested)
        {
            return;
        }

        NativeMethods.GetWindowRect(WindowHandle, out NativeMethods.RECT _window_rect);
        AppLog.Write($"面板已显示：clock={_bounds.Left},{_bounds.Top} {_bounds.Width}x{_bounds.Height} window={_window_rect.Left},{_window_rect.Top} {_window_rect.Right - _window_rect.Left}x{_window_rect.Bottom - _window_rect.Top} active={IsActive}");

        OutsideCheckTimer.Start();
    }

    /// <summary>
    /// 点击面板外面立即关闭。
    /// </summary>
    public void CloseByOutsideClick() => RequestClose("点击面板外面");

    /// <summary>
    /// 判断物理屏幕坐标是否落在面板窗口内（含四周留白）。
    /// </summary>
    public bool ContainsScreenPoint(int _x, int _y)
    {
        IntPtr _hwnd = WindowHandle;
        if (_hwnd == IntPtr.Zero || NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT _rect) is false)
        {
            return false;
        }

        return _x >= _rect.Left && _x < _rect.Right && _y >= _rect.Top && _y < _rect.Bottom;
    }

    private void PositionNearClock(ClockBounds _bounds)
    {
        NativeMethods.POINT _anchor = new()
        {
            X = _bounds.Left + _bounds.Width / 2,
            Y = _bounds.Top + _bounds.Height / 2
        };
        IntPtr _monitor = NativeMethods.MonitorFromPoint(_anchor, NativeMethods.MONITOR_DEFAULTTONEAREST);

        double _scale = 1.0;
        if (_monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(_monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint _dpi_x, out _) == 0
            && _dpi_x > 0)
        {
            _scale = _dpi_x / 96.0;
        }

        Left = Math.Round(_bounds.Right / _scale - Width + 8);
        Top = Math.Round(_bounds.Top / _scale - Height - 2 / _scale);
    }

    private void MoveNearClock(ClockBounds _bounds)
    {
        IntPtr _hwnd = new WindowInteropHelper(this).Handle;
        if (NativeMethods.GetWindowRect(_hwnd, out NativeMethods.RECT _rect) is false)
        {
            return;
        }

        int _width = _rect.Right - _rect.Left;
        int _height = _rect.Bottom - _rect.Top;
        int _x = _bounds.Right - _width + 8;
        int _y = _bounds.Top - _height - 2;

        // ? 多显示器 / 负坐标时以时钟所在屏幕为准，这里只做最简单的上边界保护
        if (_y < 0)
        {
            _y = _bounds.Bottom + 10;
        }

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, _x, _y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    #endregion

    #region 内容刷新

    private void LoadContent()
    {
        RefreshCalendar();
        RefreshLists();
        UpdateSelectedDayText();
    }

    private void RefreshCalendar()
    {
        DateOnly _month_end = DisplayMonth.AddMonths(1).AddDays(-1);

        // - 每个月格子下的彩点：按标签收集，重要→红、项目→绿、重复→紫、待办→灰
        Dictionary<DateOnly, HashSet<int>> _marks = [];
        foreach ((DateOnly _date, int _tag) in Database.GetMonthTags(DisplayMonth, _month_end))
        {
            AddMark(_marks, _date, _tag);
        }

        // - 重复实例由时光序按区间给出，缓存里有的先画，缺的在后台补
        foreach (SyncItem _repeat_item in Database.GetRepeatItems())
        {
            if (RepeatOccurrences.TryGetValue(RepeatKey(_repeat_item.Id, DisplayMonth), out HashSet<DateOnly>? _dates) is false)
            {
                continue;
            }

            foreach (DateOnly _date in _dates)
            {
                if (_date >= DisplayMonth && _date <= _month_end)
                {
                    AddMark(_marks, _date, TodoTag.Periodic);
                }
            }
        }

        CalendarBoard.DayTags = _marks.ToDictionary(
            _entry => _entry.Key,
            _entry => _entry.Value.OrderBy(_tag => _tag switch { 1 => 0, 2 => 1, 3 => 2, _ => 3 }).ToList());

        QueueRepeatLoad(DisplayMonth);
        CalendarBoard.DisplayMonth = DisplayMonth;
        CalendarBoard.SelectedDate = SelectedDate;

        MonthText.Text = DisplayMonth.ToString("yyyy年M月");
    }

    private void RefreshLists()
    {
        List<TodoRow> _day_rows = [];
        HashSet<string> _day_ids = [];

        // - 今天的面板：今天到期（含已完成）+ 逾期未完成 + 今天完成的，已完成的排到后面
        if (SelectedDate == DateOnly.FromDateTime(DateTime.Today))
        {
            foreach (SyncItem _item in Database.QueryItems("today", null)
                .OrderBy(_entry => _entry.Kind == TodoKind.Done ? 1 : 0)
                .ThenBy(_entry => _entry.DueDate, StringComparer.Ordinal))
            {
                // - 无日期又已完成的事项不进当天列表，始终留在「无日期」区
                if (_item.Kind == TodoKind.Done && string.IsNullOrEmpty(_item.DueDate))
                {
                    continue;
                }

                DateOnly? _due = DateOnly.TryParse(_item.DueDate, out DateOnly _parsed) ? _parsed : null;
                _day_rows.Add(new TodoRow
                {
                    Id = _item.Id,
                    Title = _item.Title,
                    Kind = _item.Kind,
                    Tag = _item.Tag,
                    DueDate = _due,
                    IsCompleted = _item.Kind == TodoKind.Done,
                    ShowDot = _item.TodoType is not 1,
                    TagText = ToTagText(_item.Tag),
                    HasNote = string.IsNullOrWhiteSpace(_item.Note) is false,
                    NoteText = _item.Note,
                    IsOverdue = _item.Kind != TodoKind.Done && _due < SelectedDate
                });
                _day_ids.Add(_item.Id);
            }
        }
        else
        {
            foreach (TodoItemRecord _record in Database.GetByDate(SelectedDate))
            {
                if (_day_ids.Add(_record.Id))
                {
                    _day_rows.Add(ToRow(_record, SelectedDate));
                }
            }
        }

        // - 重复事项的其它日期用时光序返回的实例补齐，已经有同名记录的跳过
        HashSet<string> _titles = [.. _day_rows.Select(_row => _row.Title)];
        DateOnly _selected_month = new(SelectedDate.Year, SelectedDate.Month, 1);
        foreach (SyncItem _repeat_item in Database.GetRepeatItems())
        {
            if (_titles.Contains(_repeat_item.Title)
                || RepeatOccurrences.TryGetValue(RepeatKey(_repeat_item.Id, _selected_month), out HashSet<DateOnly>? _dates) is false
                || _dates.Contains(SelectedDate) is false)
            {
                continue;
            }

            _day_rows.Add(new TodoRow
            {
                Id = _repeat_item.Id,
                Title = _repeat_item.Title,
                Kind = _repeat_item.Kind,
                Tag = TodoTag.Periodic,
                DueDate = SelectedDate,
                ShowDot = true,
                IsReadOnly = true,
                TagText = "重复",
                HasNote = string.IsNullOrWhiteSpace(_repeat_item.Note) is false,
                NoteText = _repeat_item.Note
            });
            _titles.Add(_repeat_item.Title);
        }

        DayItemsControl.ItemsSource = _day_rows;
        DayEmptyText.Visibility = _day_rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        List<TodoRow> _no_date_rows = Database.GetWithoutDate().Select(_record => ToRow(_record, null)).ToList();
        NoDateItemsControl.ItemsSource = _no_date_rows;
        NoDateEmptyText.Visibility = _no_date_rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDateCountText.Text = $"· {_no_date_rows.Count} 条";

        int _todo_count = _day_rows.Count(_row => _row.IsCompleted is false && _row.Kind == TodoKind.Todo);
        int _memo_count = _day_rows.Count(_row => _row.IsCompleted is false && _row.Kind == TodoKind.Memo);
        CountText.Text = $"{_todo_count} 项待办 · {_memo_count} 条备忘";
    }

    private static TodoRow ToRow(TodoItemRecord _record, DateOnly? _due_date)
    {
        return new TodoRow
        {
            Id = _record.Id,
            Title = _record.Title,
            Kind = _record.Kind,
            Tag = _record.Tag,
            DueDate = _due_date,
            IsCompleted = _record.Kind == TodoKind.Done,
            ShowDot = _record.TodoType is not 1,
            TagText = ToTagText(_record.Tag),
            HasNote = string.IsNullOrWhiteSpace(_record.Note) is false,
            NoteText = _record.Note
        };
    }

    private static void AddMark(Dictionary<DateOnly, HashSet<int>> _marks, DateOnly _date, int _tag)
    {
        if (_marks.TryGetValue(_date, out HashSet<int>? _tags) is false)
        {
            _tags = [];
            _marks[_date] = _tags;
        }

        _tags.Add(_tag);
    }

    private static string RepeatKey(string _id, DateOnly _month) => $"{_id}|{_month:yyyyMM}";

    /// <summary>
    /// 缺哪个月就向时光序补哪个月，结果只放进内存缓存。
    /// </summary>
    private void QueueRepeatLoad(DateOnly _month)
    {
        if (SyncService is null)
        {
            return;
        }

        DateOnly _end = _month.AddMonths(1).AddDays(-1);
        foreach (SyncItem _item in Database.GetRepeatItems())
        {
            string _key = RepeatKey(_item.Id, _month);
            if (RepeatOccurrences.ContainsKey(_key) || RepeatLoading.Contains(_key))
            {
                continue;
            }

            RepeatLoading.Add(_key);
            _ = LoadRepeatAsync(_item, _key, _month, _end);
        }
    }

    private async Task LoadRepeatAsync(SyncItem _item, string _key, DateOnly _from, DateOnly _to)
    {
        HashSet<DateOnly> _dates;
        try
        {
            _dates = await SyncService!.GetOccurrencesAsync(_item, _from, _to);
        }
        catch (Exception _ex)
        {
            AppLog.Write($"取重复实例失败（{_item.Title}）：{_ex.Message}");
            _dates = [];
        }
        finally
        {
            RepeatLoading.Remove(_key);
        }

        RepeatOccurrences[_key] = _dates;

        if (IsVisible is false || IsCloseRequested)
        {
            return;
        }

        RefreshCalendar();
        RefreshLists();
    }

    private static string ToTagText(int _tag) => _tag switch
    {
        TodoTag.Important => "重要",
        TodoTag.Highlight => "项目",
        TodoTag.Periodic => "重复",
        _ => ""
    };

    private void UpdateSelectedDayText()
    {
        string _input = QuickAddBox.Text.Trim();
        if (_input.Length > 0 && MessageTimer.IsEnabled is false)
        {
            // - 输入时在标题位置预览这条会被放到哪一天
            QuickInputResult _parsed = QuickInputParser.Parse(_input, DateOnly.FromDateTime(DateTime.Today));
            if (_parsed.Date is not null)
            {
                SelectedDayText.Text = $"→ {_parsed.Date.Value:M月d日}"
                    + (_parsed.Time is null ? "" : $" {_parsed.Time.Value:HH:mm}");
                return;
            }
        }

        SelectedDayText.Text = SelectedDate == DateOnly.FromDateTime(DateTime.Today)
            ? "今天 · " + SelectedDate.ToString("M月d日")
            : SelectedDate.ToString("M月d日 dddd");
    }

    #endregion

    #region 交互

    /// <summary>
    /// 勾选完成：先改本地缓存让界面立刻响应，再回写时光序，失败则回滚。
    /// </summary>
    private async void ItemRow_CheckRequested(object _sender, RoutedEventArgs _e)
    {
        if ((_e.OriginalSource as FrameworkElement)?.DataContext is not TodoRow _row)
        {
            return;
        }

        _e.Handled = true;

        if (SyncService is null)
        {
            ShowMessage("未配置时光序同步");
            return;
        }

        bool _done = _row.IsCompleted is false;
        Database.SetKind(_row.Id, _done ? TodoKind.Done : TodoKind.Todo);
        RefreshCalendar();
        RefreshLists();

        string? _error = await SyncService.SetFinishedAsync(_row.Id, _done);
        if (_error is null)
        {
            return;
        }

        // - 回写失败：把本地状态改回去，避免和时光序不一致
        Database.SetKind(_row.Id, _done ? TodoKind.Todo : TodoKind.Done);
        RefreshLists();
        ShowMessage(_error);
    }

    private void QuickAddBox_TextChanged(object _sender, TextChangedEventArgs _e)
    {
        bool _is_empty = string.IsNullOrEmpty(QuickAddBox.Text);
        QuickAddHint.Visibility = _is_empty ? Visibility.Visible : Visibility.Collapsed;
        RefreshQuickAddPreview();
        UpdateSelectedDayText();
    }

    /// <summary>
    /// 输入框文字用叠加层绘制，识别出的时间片段标成橙色。
    /// </summary>
    private void RefreshQuickAddPreview()
    {
        string _text = QuickAddBox.Text;
        QuickAddPreview.Inlines.Clear();

        if (_text.Length == 0)
        {
            QuickAddPreview.Visibility = Visibility.Collapsed;
            return;
        }

        QuickInputResult _parsed = QuickInputParser.Parse(_text, DateOnly.FromDateTime(DateTime.Today));

        // - 日期词标橙、时间词标蓝，按在原句中的位置切分
        List<(int Start, int Length, System.Windows.Media.Brush Color)> _ranges = [];
        foreach ((string _hit, byte _r, byte _g, byte _b) in new[]
        {
            (_parsed.DateText, (byte)0xE8, (byte)0x7E, (byte)0x2E),
            (_parsed.TimeText, (byte)0x2E, (byte)0x7B, (byte)0xE8)
        })
        {
            if (string.IsNullOrEmpty(_hit) is false)
            {
                int _index = _text.IndexOf(_hit, StringComparison.Ordinal);
                if (_index >= 0)
                {
                    _ranges.Add((_index, _hit.Length,
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(_r, _g, _b))));
                }
            }
        }

        _ranges.Sort((_a, _b) => _a.Start.CompareTo(_b.Start));

        int _cursor = 0;
        foreach ((int _start, int _length, System.Windows.Media.Brush _color) in _ranges)
        {
            if (_start < _cursor)
            {
                continue;
            }

            if (_start > _cursor)
            {
                QuickAddPreview.Inlines.Add(new Run(_text[_cursor.._start]));
            }

            QuickAddPreview.Inlines.Add(new Run(_text.Substring(_start, _length)) { Foreground = _color });
            _cursor = _start + _length;
        }

        if (_cursor < _text.Length)
        {
            QuickAddPreview.Inlines.Add(new Run(_text[_cursor..]));
        }

        QuickAddPreview.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 回车新建：文本里的日期/时间在本地解析，再写到时光序。
    /// </summary>
    private async void QuickAddBox_KeyDown(object _sender, KeyEventArgs _e)
    {
        if (_e.Key is not Key.Enter)
        {
            return;
        }

        _e.Handled = true;

        string _text = QuickAddBox.Text.Trim();
        if (_text.Length == 0)
        {
            return;
        }

        if (SyncService is null)
        {
            ShowMessage("未配置时光序同步");
            return;
        }

        QuickAddBox.IsEnabled = false;
        string? _error = await SyncService.CreateAsync(_text);
        QuickAddBox.IsEnabled = true;

        if (_error is not null)
        {
            ShowMessage(_error);
            return;
        }

        QuickInputResult _parsed = QuickInputParser.Parse(_text, DateOnly.FromDateTime(DateTime.Today));
        QuickAddBox.Clear();

        if (_parsed.Date is not null)
        {
            SelectedDate = _parsed.Date.Value;
            DisplayMonth = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);
        }

        LoadContent();
        QuickAddBox.Focus();
    }

    /// <summary>
    /// 在标题行短暂显示一条提示：失败为红、成功为绿。
    /// </summary>
    private void ShowMessage(string _message, bool _is_error = true)
    {
        SelectedDayText.Text = _message;
        SelectedDayText.Foreground = new System.Windows.Media.SolidColorBrush(_is_error
            ? System.Windows.Media.Color.FromRgb(0xD9, 0x3A, 0x3A)
            : System.Windows.Media.Color.FromRgb(0x2E, 0x9E, 0x5B));
        MessageTimer.Stop();
        MessageTimer.Start();
    }

    private void CalendarBoard_DateSelected(object? _sender, DateOnly _date)
    {
        SelectedDate = _date;
        DisplayMonth = new DateOnly(_date.Year, _date.Month, 1);
        LoadContent();
    }

    private void SettingsButton_Click(object _sender, RoutedEventArgs _e)
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 手动同步一次时光序，成功后刷新列表。
    /// </summary>
    private async void Refresh_Click(object _sender, RoutedEventArgs _e)
    {
        if (SyncService is null)
        {
            ShowMessage("未配置时光序同步");
            return;
        }

        RefreshButton.IsEnabled = false;
        try
        {
            (bool _ok, string _message) = await SyncService.SyncNowAsync();
            if (_ok is false)
            {
                ShowMessage(_message);
                return;
            }

            LoadContent();
            ShowMessage(_message, _is_error: false);
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void MonthPrev_Click(object _sender, RoutedEventArgs _e)
    {
        DisplayMonth = DisplayMonth.AddMonths(-1);
        RefreshCalendar();
    }

    private void MonthNext_Click(object _sender, RoutedEventArgs _e)
    {
        DisplayMonth = DisplayMonth.AddMonths(1);
        RefreshCalendar();
    }

    private void Today_Click(object _sender, RoutedEventArgs _e)
    {
        SelectedDate = DateOnly.FromDateTime(DateTime.Today);
        DisplayMonth = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);
        LoadContent();
    }

    #endregion

    #region 关闭

    private void ActivationRetryTimer_Tick(object? _sender, EventArgs _e)
    {
        if (IsCloseRequested || WindowHandle == IntPtr.Zero)
        {
            ActivationRetryTimer.Stop();
            return;
        }

        if (GetForegroundWindowSafe() == WindowHandle || ActivationRetryCount >= 10)
        {
            ActivationRetryTimer.Stop();
            return;
        }

        ActivationRetryCount++;
        NativeMethods.ForceForeground(WindowHandle);
        Activate();
    }

    private void OutsideCheckTimer_Tick(object? _sender, EventArgs _e)
    {
        if (AutoCloseEnabled is false || IsCloseRequested || WindowHandle == IntPtr.Zero)
        {
            OutsideCheckTimer.Stop();
            return;
        }

        if ((DateTime.Now - ShownAt).TotalMilliseconds < GracePeriodMilliseconds)
        {
            return;
        }

        if (GetForegroundWindowSafe() == WindowHandle)
        {
            ForegroundMissCount = 0;
            return;
        }

        // - 连续两次不在前台才关闭，避免瞬时切换误判
        ForegroundMissCount++;
        if (ForegroundMissCount >= 2)
        {
            RequestClose("面板失去前台");
        }
    }

    private void FlyoutWindow_Deactivated(object? _sender, EventArgs _e)
    {
        if (AutoCloseEnabled is false || IsVisible is false)
        {
            return;
        }

        if ((DateTime.Now - ShownAt).TotalMilliseconds < GracePeriodMilliseconds)
        {
            return;
        }

        RequestClose("面板失去焦点");
    }

    private void FlyoutWindow_PreviewKeyDown(object _sender, KeyEventArgs _e)
    {
        if (_e.Key is Key.Escape)
        {
            RequestClose("按下 Esc");
        }
    }

    private void FlyoutWindow_Closed(object? _sender, EventArgs _e)
    {
        OutsideCheckTimer.Stop();
        ActivationRetryTimer.Stop();
        MessageTimer.Stop();
        AppLog.Write("面板已关闭");
    }

    private void RequestClose(string _reason)
    {
        if (IsCloseRequested)
        {
            return;
        }

        IsCloseRequested = true;
        OutsideCheckTimer.Stop();
        ActivationRetryTimer.Stop();
        AppLog.Write($"{_reason}，关闭面板");

        try
        {
            Close();
        }
        catch (InvalidOperationException)
        {
            // - 窗口已在关闭过程中，忽略重复关闭
        }
    }

    private static IntPtr GetForegroundWindowSafe()
    {
        try
        {
            return NativeMethods.GetForegroundWindow();
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    #endregion
}
