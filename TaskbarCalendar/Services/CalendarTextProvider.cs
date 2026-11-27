using System;
using System.Globalization;

namespace TaskbarCalendar.Services;

/// <summary>
/// 面板上展示的日期文本，公历 + 农历 + 干支。
/// </summary>
public static class CalendarTextProvider
{
    private static readonly string[] WeekdayNames = ["星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"];

    private static readonly string[] LunarMonthNames = ["正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月"];

    private static readonly string[] LunarDayNames =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    ];

    private static readonly string[] HeavenlyStems = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];

    private static readonly string[] EarthlyBranches = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];

    /// <summary>
    /// 例如 10:06。
    /// </summary>
    public static string GetTimeLine(DateTime _date) => _date.ToString("HH:mm");

    /// <summary>
    /// 例如 2026年11月23日 星期一。
    /// </summary>
    public static string GetDateLine(DateTime _date) => $"{_date:yyyy年M月d日} {WeekdayNames[(int)_date.DayOfWeek]}";

    /// <summary>
    /// 例如 十月十五 · 丙午年。
    /// </summary>
    public static string GetLunarLine(DateTime _date)
    {
        ChineseLunisolarCalendar _calendar = new();
        int _lunar_year = _calendar.GetYear(_date);
        int _month = _calendar.GetMonth(_date);
        int _day = _calendar.GetDayOfMonth(_date);

        int _leap_month = _calendar.GetLeapMonth(_lunar_year);
        bool _is_leap = _leap_month > 0 && _month == _leap_month;
        if (_leap_month > 0 && _month >= _leap_month)
        {
            _month -= 1;
        }

        string _month_text = LunarMonthNames[Math.Clamp(_month - 1, 0, LunarMonthNames.Length - 1)];
        string _day_text = LunarDayNames[Math.Clamp(_day - 1, 0, LunarDayNames.Length - 1)];
        string _leap_text = _is_leap ? "闰" : string.Empty;

        return $"{_leap_text}{_month_text}{_day_text} · {GetSexagenaryYear(_lunar_year)}年";
    }

    /// <summary>
    /// 例如 2026年11月。
    /// </summary>
    public static string GetMonthLine(DateTime _date) => _date.ToString("yyyy年M月");

    private static string GetSexagenaryYear(int _year)
    {
        int _offset = ((_year - 4) % 60 + 60) % 60;
        return $"{HeavenlyStems[_offset % 10]}{EarthlyBranches[_offset % 12]}";
    }
}
