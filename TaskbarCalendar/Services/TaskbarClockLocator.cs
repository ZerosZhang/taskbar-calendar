using System;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using TaskbarCalendar.Interop;

namespace TaskbarCalendar.Services;

/// <summary>
/// 定位任务栏时钟的屏幕区域。
/// Win11 通过 UI Automation 找到任务栏上的时钟按钮；Win10 回退到 TrayClockWClass 原生窗口。
/// </summary>
public static class TaskbarClockLocator
{
    private const string TaskbarClassName = "Shell_TrayWnd";
    private const string TrayNotifyClassName = "TrayNotifyWnd";
    private const string OmniClockClassName = "SystemTray.OmniButton";
    private const string LegacyClockClassName = "TrayClockWClass";

    private static readonly Regex ClockNamePattern = new(@"\d{1,2}[:：]\d{2}", RegexOptions.Compiled);

    /// <summary>
    /// 查找当前任务栏时钟区域，找不到时返回 null。
    /// </summary>
    public static ClockBounds? Locate()
    {
        try
        {
            return LocateByUia() ?? LocateLegacyClock();
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ClockBounds? LocateByUia()
    {
        AutomationElement _root = AutomationElement.RootElement;
        PropertyCondition _taskbar_condition = new(AutomationElement.ClassNameProperty, TaskbarClassName);
        AutomationElement? _taskbar = _root.FindFirst(TreeScope.Children, _taskbar_condition);
        if (_taskbar is null)
        {
            return null;
        }

        // ! Step1: Win11 22H2 及以后，时钟按钮的类名固定为 SystemTray.OmniButton
        PropertyCondition _clock_condition = new(AutomationElement.ClassNameProperty, OmniClockClassName);
        AutomationElement? _clock = _taskbar.FindFirst(TreeScope.Descendants, _clock_condition);

        // - 找不到时退化：在任务栏按钮里取最右侧、名称含时间的那个
        _clock ??= FindClockByTimePattern(_taskbar);

        if (_clock is null)
        {
            return null;
        }

        System.Windows.Rect _rect = _clock.Current.BoundingRectangle;
        if (_rect.IsEmpty || _rect.Width < 8 || _rect.Height < 8)
        {
            return null;
        }

        return new ClockBounds((int)_rect.Left, (int)_rect.Top, (int)_rect.Right, (int)_rect.Bottom);
    }

    private static AutomationElement? FindClockByTimePattern(AutomationElement _taskbar)
    {
        AutomationElementCollection _elements = _taskbar.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        AutomationElement? _best = null;
        double _best_right = double.NegativeInfinity;

        foreach (AutomationElement _element in _elements)
        {
            if (_element.Current.ControlType != ControlType.Button)
            {
                continue;
            }

            string _name = _element.Current.Name;
            if (string.IsNullOrEmpty(_name) || ClockNamePattern.IsMatch(_name) is false)
            {
                continue;
            }

            System.Windows.Rect _rect = _element.Current.BoundingRectangle;
            if (_rect.IsEmpty || _rect.Right <= _best_right)
            {
                continue;
            }

            _best = _element;
            _best_right = _rect.Right;
        }

        return _best;
    }

    private static ClockBounds? LocateLegacyClock()
    {
        IntPtr _taskbar = NativeMethods.FindWindow(TaskbarClassName, null);
        if (_taskbar == IntPtr.Zero)
        {
            return null;
        }

        IntPtr _tray_notify = NativeMethods.FindWindowEx(_taskbar, IntPtr.Zero, TrayNotifyClassName, null);
        if (_tray_notify == IntPtr.Zero)
        {
            return null;
        }

        IntPtr _clock = NativeMethods.FindWindowEx(_tray_notify, IntPtr.Zero, LegacyClockClassName, null);
        if (_clock == IntPtr.Zero || NativeMethods.GetWindowRect(_clock, out NativeMethods.RECT _rect) is false)
        {
            return null;
        }

        return new ClockBounds(_rect.Left, _rect.Top, _rect.Right, _rect.Bottom);
    }
}
