using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskbarCalendar.Views.Controls;

/// <summary>
/// 统一的事项行控件：勾选框/备忘圆点 + 标题 + 分类标签。
/// 标签合并了原优先级与重复：重要（红）/ 项目（绿）/ 周期（紫）。
/// </summary>
public partial class ItemRowView : UserControl
{
    public static readonly RoutedEvent CheckRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(CheckRequested),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(ItemRowView));

    public static readonly RoutedEvent TagRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(TagRequested),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(ItemRowView));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ItemRowView),
        new PropertyMetadata("", OnVisualPropertyChanged));

    /// <summary>
    /// 0 备忘 / 1 待办 / 2 已完成。
    /// </summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(int), typeof(ItemRowView),
        new PropertyMetadata(0, OnVisualPropertyChanged));

    /// <summary>
    /// 标签：0 无 / 1 重要 / 2 项目 / 3 周期。
    /// </summary>
    public static readonly DependencyProperty ItemTagProperty = DependencyProperty.Register(
        nameof(ItemTag), typeof(int), typeof(ItemRowView),
        new PropertyMetadata(0, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsOverdueProperty = DependencyProperty.Register(
        nameof(IsOverdue), typeof(bool), typeof(ItemRowView),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty HasNoteProperty = DependencyProperty.Register(
        nameof(HasNote), typeof(bool), typeof(ItemRowView),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty NoteTextProperty = DependencyProperty.Register(
        nameof(NoteText), typeof(string), typeof(ItemRowView),
        new PropertyMetadata("", OnVisualPropertyChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(ItemRowView),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// 用实心圆点代替勾选框（日程、重复类事项）。
    /// </summary>
    public static readonly DependencyProperty ShowDotProperty = DependencyProperty.Register(
        nameof(ShowDot), typeof(bool), typeof(ItemRowView),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// 标签文字；为空时按 ItemTag 取默认文字。
    /// </summary>
    public static readonly DependencyProperty TagLabelProperty = DependencyProperty.Register(
        nameof(TagLabel), typeof(string), typeof(ItemRowView),
        new PropertyMetadata("", OnVisualPropertyChanged));

    /// <summary>
    /// 点标签是否响应（面板里用来循环切换标签）。
    /// </summary>
    public static readonly DependencyProperty AllowTagClickProperty = DependencyProperty.Register(
        nameof(AllowTagClick), typeof(bool), typeof(ItemRowView),
        new PropertyMetadata(false, OnVisualPropertyChanged));

    public ItemRowView()
    {
        InitializeComponent();
        RefreshVisual();
    }

    public event RoutedEventHandler CheckRequested
    {
        add => AddHandler(CheckRequestedEvent, value);
        remove => RemoveHandler(CheckRequestedEvent, value);
    }

    public event RoutedEventHandler TagRequested
    {
        add => AddHandler(TagRequestedEvent, value);
        remove => RemoveHandler(TagRequestedEvent, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public int Kind
    {
        get => (int)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public int ItemTag
    {
        get => (int)GetValue(ItemTagProperty);
        set => SetValue(ItemTagProperty, value);
    }

    public string TagLabel
    {
        get => (string)GetValue(TagLabelProperty);
        set => SetValue(TagLabelProperty, value);
    }

    public bool IsOverdue
    {
        get => (bool)GetValue(IsOverdueProperty);
        set => SetValue(IsOverdueProperty, value);
    }

    public bool HasNote
    {
        get => (bool)GetValue(HasNoteProperty);
        set => SetValue(HasNoteProperty, value);
    }

    public string NoteText
    {
        get => (string)GetValue(NoteTextProperty);
        set => SetValue(NoteTextProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowDot
    {
        get => (bool)GetValue(ShowDotProperty);
        set => SetValue(ShowDotProperty, value);
    }

    public bool AllowTagClick
    {
        get => (bool)GetValue(AllowTagClickProperty);
        set => SetValue(AllowTagClickProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject _sender, DependencyPropertyChangedEventArgs _e)
    {
        ((ItemRowView)_sender).RefreshVisual();
    }

    private void RefreshVisual()
    {
        if (TitleText is null)
        {
            return;
        }

        bool _is_completed = Kind == 2;
        bool _is_memo = Kind == 0;

        // - 圆点类（日程/重复）不表达完成状态，始终按普通备忘显示
        bool _show_dot = ShowDot || _is_memo;
        bool _visual_completed = _is_completed && _show_dot is false;

        TitleText.Text = Title;
        TitleText.TextDecorations = _visual_completed ? TextDecorations.Strikethrough : null;
        TitleText.Foreground = new SolidColorBrush(_visual_completed
            ? Color.FromRgb(0x8A, 0x8F, 0x98)
            : IsOverdue ? Color.FromRgb(0xD9, 0x3A, 0x3A)
            : _is_memo ? Color.FromRgb(0x7A, 0x7F, 0x87) : Color.FromRgb(0x2A, 0x2A, 0x2E));

        // - 只读（重复投影行）也保留勾选框/圆点，只是不可点
        CheckArea.Visibility = Visibility.Visible;
        MemoDot.Visibility = _show_dot && _visual_completed is false ? Visibility.Visible : Visibility.Collapsed;
        DoneCheck.Visibility = _show_dot ? Visibility.Collapsed : Visibility.Visible;
        DoneCheck.IsChecked = _is_completed;

        NoteIcon.Visibility = HasNote ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = HasNote && string.IsNullOrWhiteSpace(NoteText) is false ? NoteText : null;

        bool _show_tag = ItemTag is > 0 and <= 3;
        TagPill.Visibility = _show_tag ? Visibility.Visible : Visibility.Collapsed;
        if (_show_tag)
        {
            (TagText.Text, TagText.Foreground, TagPill.Background) = ItemTag switch
            {
                1 => ("重要", new SolidColorBrush(Color.FromRgb(0xD9, 0x3A, 0x3A)), new SolidColorBrush(Color.FromRgb(0xFB, 0xE9, 0xE9))),
                2 => ("项目", new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B)), new SolidColorBrush(Color.FromRgb(0xE6, 0xF5, 0xEC))),
                _ => ("周期", new SolidColorBrush(Color.FromRgb(0x6B, 0x3F, 0xE0)), new SolidColorBrush(Color.FromRgb(0xED, 0xE1, 0xFF)))
            };

            // - 面板里用分类名（项目/周期）替代默认文字
            if (string.IsNullOrEmpty(TagLabel) is false)
            {
                TagText.Text = TagLabel;
            }
        }

        TagPill.Cursor = AllowTagClick && IsReadOnly is false ? Cursors.Hand : Cursors.Arrow;
        CheckArea.Cursor = IsReadOnly ? Cursors.Arrow : Cursors.Hand;
    }

    private void CheckArea_MouseLeftButtonDown(object _sender, MouseButtonEventArgs _e)
    {
        if (IsReadOnly)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(CheckRequestedEvent, this));
        _e.Handled = true;
    }

    private void TagPill_MouseLeftButtonDown(object _sender, MouseButtonEventArgs _e)
    {
        if (AllowTagClick is false || IsReadOnly)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(TagRequestedEvent, this));
        _e.Handled = true;
    }
}
