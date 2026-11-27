using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Linq;
using TaskbarCalendar.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TaskbarCalendar.Views.Controls;

/// <summary>
/// 月历控件：日期格子为圆角正方形，尺寸由 CellSize 决定，控件整体尺寸随之计算。
/// 主窗口日历页与任务栏面板共用。
/// </summary>
public sealed class CalendarView : UserControl
{
    public static readonly DependencyProperty CellSizeProperty = DependencyProperty.Register(
        nameof(CellSize), typeof(double), typeof(CalendarView),
        new PropertyMetadata(30.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty DayFontSizeProperty = DependencyProperty.Register(
        nameof(DayFontSize), typeof(double), typeof(CalendarView),
        new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    private readonly StackPanel Root;
    private readonly UniformGrid WeekHeader;
    private readonly Grid DayGrid;

    private DateOnly Month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private DateOnly? Selected;

    /// <summary>
    /// 选中的日期变化（点击格子触发）。
    /// </summary>
    public event EventHandler<DateOnly>? DateSelected;

    public CalendarView()
    {
        Root = new StackPanel();

        WeekHeader = new UniformGrid { Columns = 7 };
        foreach (string _name in new[] { "一", "二", "三", "四", "五", "六", "日" })
        {
            WeekHeader.Children.Add(new TextBlock
            {
                Text = _name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }

        Root.Children.Add(WeekHeader);

        DayGrid = new Grid();
        Root.Children.Add(DayGrid);

        Content = Root;
        ApplyingLayout();
        Rebuild();
    }

    /// <summary>
    /// 每个日期卡片的边长（正方形）。
    /// </summary>
    public double CellSize
    {
        get => (double)GetValue(CellSizeProperty);
        set => SetValue(CellSizeProperty, value);
    }

    /// <summary>
    /// 日期数字字号；0 表示按格子尺寸自动计算。
    /// </summary>
    public double DayFontSize
    {
        get => (double)GetValue(DayFontSizeProperty);
        set => SetValue(DayFontSizeProperty, value);
    }

    public DateOnly DisplayMonth
    {
        get => Month;
        set
        {
            Month = new DateOnly(value.Year, value.Month, 1);
            Rebuild();
        }
    }

    public DateOnly? SelectedDate
    {
        get => Selected;
        set
        {
            Selected = value;
            Rebuild();
        }
    }

    /// <summary>
    /// 每格下方的小圆点：日期 → 标签集合（1 重要 / 2 项目 / 3 重复 / 0 待办）。
    /// </summary>
    public Dictionary<DateOnly, List<int>> DayTags { get; set; } = [];

    public void Refresh()
    {
        Rebuild();
    }

    private static void OnLayoutPropertyChanged(DependencyObject _sender, DependencyPropertyChangedEventArgs _e)
    {
        CalendarView _view = (CalendarView)_sender;
        _view.ApplyingLayout();
        _view.Rebuild();
    }

    private void ApplyingLayout()
    {
        double _cell = CellSize + 6;
        double _font = DayFontSize > 0 ? DayFontSize : Math.Max(10, Math.Round(CellSize * 0.36));
        foreach (UIElement _child in WeekHeader.Children)
        {
            if (_child is TextBlock _label)
            {
                _label.FontSize = _font;
            }
        }

        Root.Width = Math.Round(_cell * 7);
        WeekHeader.Width = Root.Width;
        WeekHeader.Height = 20;
        DayGrid.Width = Root.Width;
        DayGrid.Height = Math.Round(_cell * 6);
    }

    private static Color TagColor(int _tag) => _tag switch
    {
        1 => Color.FromRgb(0xD9, 0x3A, 0x3A),
        2 => Color.FromRgb(0x2E, 0x9E, 0x5B),
        3 => Color.FromRgb(0x6B, 0x3F, 0xE0),
        _ => Color.FromRgb(0x9A, 0xA0, 0xA8)
    };

    private static Color WithAlpha(Color _color) => Color.FromArgb(0xE6, _color.R, _color.G, _color.B);

    private void Rebuild()
    {
        DayGrid.Children.Clear();
        DayGrid.RowDefinitions.Clear();
        DayGrid.ColumnDefinitions.Clear();

        if (DayGrid.Width <= 0)
        {
            ApplyingLayout();
        }

        for (int _column = 0; _column < 7; _column++)
        {
            DayGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        for (int _row = 0; _row < 6; _row++)
        {
            DayGrid.RowDefinitions.Add(new RowDefinition());
        }

        DateOnly _today = DateOnly.FromDateTime(DateTime.Today);
        int _offset = ((int)Month.DayOfWeek + 6) % 7;
        DateOnly _start = Month.AddDays(-_offset);

        for (int _index = 0; _index < 42; _index++)
        {
            DateOnly _date = _start.AddDays(_index);
            Border _cell = BuildCell(_date, _today);
            Grid.SetRow(_cell, _index / 7);
            Grid.SetColumn(_cell, _index % 7);
            DayGrid.Children.Add(_cell);
        }
    }

    private Border BuildCell(DateOnly _date, DateOnly _today)
    {
        bool _is_selected = Selected == _date;
        bool _is_outside = _date.Month != Month.Month;
        bool _is_today = _date == _today;

        DayTags.TryGetValue(_date, out List<int>? _tags);
        bool _is_repeat = _tags is not null && _tags.Contains(TodoTag.Periodic);
        List<int> _dots_tags = _tags is null
            ? []
            : [.. _tags.Where(_tag => _tag != TodoTag.Periodic)
                .OrderBy(_tag => _tag switch { 1 => 0, 2 => 1, _ => 2 })
                .Take(3)];

        Color _background = _is_selected
            ? Color.FromRgb(0xFF, 0xFF, 0xFF)
            : _is_repeat
                ? (_is_outside ? Color.FromRgb(0xF7, 0xF4, 0xFD) : Color.FromRgb(0xEF, 0xE9, 0xFB))
                : _is_outside ? Color.FromRgb(0xF8, 0xF9, 0xFB) : Color.FromRgb(0xF2, 0xF4, 0xF8);

        Border _chip = new()
        {
            Width = CellSize,
            Height = CellSize,
            CornerRadius = new CornerRadius(Math.Round(CellSize * 0.22)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Background = new SolidColorBrush(_background),
            // - 选中用描边而不是实心蓝底，保证下方圆点颜色不失真
            BorderBrush = _is_selected
                ? new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0xFF))
                : _is_today ? new SolidColorBrush(Color.FromRgb(0x5B, 0x9B, 0xFF)) : Brushes.Transparent,
            BorderThickness = _is_selected ? new Thickness(1.6) : _is_today ? new Thickness(1) : new Thickness(0),
            Tag = _date
        };

        Grid _content = new();

        _content.Children.Add(new TextBlock
        {
            Text = _date.Day.ToString(),
            FontSize = DayFontSize > 0 ? DayFontSize : Math.Max(10, Math.Round(CellSize * 0.36)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, _dots_tags.Count > 0 ? 7 : 0),
            FontWeight = _is_selected ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(_is_selected
                ? Color.FromRgb(0x2F, 0x6B, 0xFF)
                : _is_outside ? Color.FromRgb(0xC9, 0xCC, 0xD2) : Color.FromRgb(0x3C, 0x40, 0x43))
        });

        // - 日期下方的小圆点：红重要 / 绿项目 / 灰待办；重复事项只用底色体现
        if (_dots_tags.Count > 0)
        {
            StackPanel _dots = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 1),
                IsHitTestVisible = false
            };

            foreach (int _tag in _dots_tags)
            {
                _dots.Children.Add(new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Margin = new Thickness(1.5, 0, 1.5, 0),
                    Fill = new SolidColorBrush(TagColor(_tag))
                });
            }

            _content.Children.Add(_dots);
        }

        _chip.Child = _content;
        _chip.MouseLeftButtonDown += (_, _e) =>
        {
            if (_chip.Tag is not DateOnly _clicked)
            {
                return;
            }

            DateSelected?.Invoke(this, _clicked);
            _e.Handled = true;
        };

        return _chip;
    }
}
