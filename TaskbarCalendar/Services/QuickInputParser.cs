using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TaskbarCalendar.Services;

/// <summary>
/// 快速输入的解析结果：去掉时间词后的标题 + 识别到的日期与时刻。
/// </summary>
public readonly record struct QuickInputResult(
    string Title, DateOnly? Date, TimeOnly? Time = null, string TimeText = "", string DateText = "");

/// <summary>
/// 把输入里的中文日期词识别成具体日期，粒度到天。
/// 规则按"长词优先"排列，命中即取走，只解析第一个日期。
/// </summary>
public static class QuickInputParser
{
    private sealed record Rule(Regex Pattern, Func<Match, DateOnly, DateOnly?> Resolve);

    public static QuickInputResult Parse(string _input, DateOnly _today)
    {
        string _text = _input.Trim();
        if (_text.Length == 0)
        {
            return new QuickInputResult("", null);
        }

        foreach (Rule _rule in BuildRules())
        {
            Match _match = _rule.Pattern.Match(_text);
            if (_match.Success is false)
            {
                continue;
            }

            DateOnly? _date = _rule.Resolve(_match, _today);
            if (_date is null)
            {
                continue;
            }

            string _rest = _text.Remove(_match.Index, _match.Length);
            (string _title_text, TimeOnly? _time, string _time_text) = ParseTime(_rest);
            return new QuickInputResult(CleanTitle(_title_text), _date, _time, _time_text, _match.Value);
        }

        (string _without_time, TimeOnly? _time_only, string _matched) = ParseTime(_text);
        return new QuickInputResult(CleanTitle(_without_time), null, _time_only, _matched, "");
    }

    /// <summary>
    /// 识别「下午三点 / 15:30 / 9点20分」这类时间词，返回去掉后的文本与时刻。
    /// </summary>
    private static (string Text, TimeOnly? Time, string Matched) ParseTime(string _text)
    {
        Match _numeric = Regex.Match(_text, @"(?<!\d)(\d{1,2})\s*[:：]\s*(\d{2})(?!\d)");
        if (_numeric.Success
            && int.TryParse(_numeric.Groups[1].Value, out int _hour)
            && int.TryParse(_numeric.Groups[2].Value, out int _minute)
            && _hour is >= 0 and <= 23 && _minute is >= 0 and <= 59)
        {
            return (_text.Remove(_numeric.Index, _numeric.Length),
                new TimeOnly(_hour, _minute), _numeric.Value);
        }

        Match _chinese = Regex.Match(
            _text,
            @"(凌晨|早上|上午|中午|下午|傍晚|晚上)?\s*([一二三四五六七八九十两\d]{1,3})\s*[点时時](?:([一二三四五六七八九十\d]{1,2})\s*分|半)?");
        int? _h = _chinese.Success ? ToNumber(_chinese.Groups[2].Value) : null;
        if (_h is null || _h is < 0 or > 24)
        {
            return (_text, null, "");
        }

        string _period = _chinese.Groups[1].Value;
        int _m = 0;
        if (_chinese.Groups[3].Success)
        {
            int? _parsed_minute = ToNumber(_chinese.Groups[3].Value);
            if (_parsed_minute is null or < 0 or > 59)
            {
                return (_text, null, "");
            }

            _m = _parsed_minute.Value;
        }
        else if (_chinese.Value.Contains('半'))
        {
            _m = 30;
        }

        int _hour24 = _period switch
        {
            "下午" or "傍晚" or "晚上" => _h.Value < 12 ? _h.Value + 12 : _h.Value,
            "中午" => _h.Value < 11 ? _h.Value + 12 : _h.Value,
            "凌晨" => _h.Value == 12 ? 0 : _h.Value,
            _ => _h.Value
        };

        if (_hour24 is < 0 or > 23)
        {
            return (_text, null, "");
        }

        return (_text.Remove(_chinese.Index, _chinese.Length),
            new TimeOnly(_hour24, _m), _chinese.Value);
    }

    private static IEnumerable<Rule> BuildRules()
    {
        yield return new Rule(
            new Regex("大后天"),
            (_, _today) => _today.AddDays(3));

        yield return new Rule(
            new Regex("后天"),
            (_, _today) => _today.AddDays(2));

        yield return new Rule(
            new Regex("明天|明日|明早"),
            (_, _today) => _today.AddDays(1));

        yield return new Rule(
            new Regex("今天|今日"),
            (_, _today) => _today);

        yield return new Rule(
            new Regex(@"(\d{1,3})\s*天(?:以后|之后|后)"),
            (_match, _today) =>
            {
                int _days = int.Parse(_match.Groups[1].Value, CultureInfo.InvariantCulture);
                return _today.AddDays(_days);
            });

        yield return new Rule(
            new Regex(@"下(?:个)?(?:周|星期|礼拜)([一二三四五六日天1-7])"),
            (_match, _today) =>
            {
                int? _weekday = ToWeekday(_match.Groups[1].Value);
                if (_weekday is null)
                {
                    return null;
                }

                DateOnly _monday = _today.AddDays(1 - TodayIndex(_today));
                return _monday.AddDays(7 + _weekday.Value - 1);
            });

        yield return new Rule(
            new Regex(@"(\d{1,2})\s*月\s*(\d{1,2})\s*[日号]"),
            (_match, _today) =>
            {
                int _month = int.Parse(_match.Groups[1].Value, CultureInfo.InvariantCulture);
                int _day = int.Parse(_match.Groups[2].Value, CultureInfo.InvariantCulture);
                DateOnly? _date = SafeDate(_today.Year, _month, _day);
                if (_date is null)
                {
                    return null;
                }

                return _date.Value < _today ? SafeDate(_today.Year + 1, _month, _day) : _date;
            });

        yield return new Rule(
            new Regex(@"下(?:个)?月\s*(\d{1,2})\s*[日号]"),
            (_match, _today) =>
            {
                DateOnly _first = new(_today.Year, _today.Month, 1);
                DateOnly _next_month = _first.AddMonths(1);
                int _day = int.Parse(_match.Groups[1].Value, CultureInfo.InvariantCulture);
                return SafeDate(_next_month.Year, _next_month.Month, _day);
            });

        yield return new Rule(
            new Regex(@"(?:周|星期|礼拜)([一二三四五六日天1-7])"),
            (_match, _today) =>
            {
                int? _weekday = ToWeekday(_match.Groups[1].Value);
                if (_weekday is null)
                {
                    return null;
                }

                int _offset = _weekday.Value - TodayIndex(_today);
                if (_offset < 0)
                {
                    _offset += 7;
                }

                return _today.AddDays(_offset);
            });

        yield return new Rule(
            new Regex(@"(\d{1,2})\s*[日号]"),
            (_match, _today) =>
            {
                int _day = int.Parse(_match.Groups[1].Value, CultureInfo.InvariantCulture);
                DateOnly? _date = SafeDate(_today.Year, _today.Month, _day);
                if (_date is null)
                {
                    return null;
                }

                if (_date.Value >= _today)
                {
                    return _date;
                }

                DateOnly _next_month = new DateOnly(_today.Year, _today.Month, 1).AddMonths(1);
                return SafeDate(_next_month.Year, _next_month.Month, _day);
            });
    }

    /// <summary>
    /// 星期一为 1、星期日为 7。
    /// </summary>
    /// <summary>
    /// 中文数字转整数，支持 1–59（含「两」「十」「二十一」这类写法）。
    /// </summary>
    private static int? ToNumber(string _text)
    {
        if (int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int _arabic))
        {
            return _arabic;
        }

        const string Digits = "零一二三四五六七八九";
        string _value = _text.Replace("两", "二").Replace("0", "零")
            .Replace("1", "一").Replace("2", "二").Replace("3", "三").Replace("4", "四")
            .Replace("5", "五").Replace("6", "六").Replace("7", "七").Replace("8", "八").Replace("9", "九");

        if (_value.Length == 0 || _value.Any(_char => Digits.Contains(_char) is false && _char != '十'))
        {
            return null;
        }

        int _ten = _value.IndexOf('十');
        if (_ten < 0)
        {
            return Digits.IndexOf(_value[0]);
        }

        int _tens = _ten == 0 ? 1 : Digits.IndexOf(_value[0]);
        if (_tens < 0)
        {
            return null;
        }

        string _ones_part = _value[(_ten + 1)..];
        int _ones = _ones_part.Length == 0 ? 0 : Digits.IndexOf(_ones_part[0]);
        return _ones < 0 ? null : _tens * 10 + _ones;
    }

    private static int TodayIndex(DateOnly _today) => ((int)_today.DayOfWeek + 6) % 7 + 1;

    private static int? ToWeekday(string _text)
    {
        if (int.TryParse(_text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int _number))
        {
            return _number is >= 1 and <= 7 ? _number : null;
        }

        return _text switch
        {
            "一" => 1,
            "二" => 2,
            "三" => 3,
            "四" => 4,
            "五" => 5,
            "六" => 6,
            "日" or "天" => 7,
            _ => null
        };
    }

    private static DateOnly? SafeDate(int _year, int _month, int _day)
    {
        if (_month is < 1 or > 12)
        {
            return null;
        }

        int _days_in_month = DateTime.DaysInMonth(_year, _month);
        if (_day < 1 || _day > _days_in_month)
        {
            return null;
        }

        return new DateOnly(_year, _month, _day);
    }

    private static string CleanTitle(string _text)
    {
        string _cleaned = Regex.Replace(_text, @"\s+", " ").Trim();
        _cleaned = _cleaned.Trim(' ', ',', '，', '。', '；', ';', '、', ':', '：', '-');
        return _cleaned;
    }
}
