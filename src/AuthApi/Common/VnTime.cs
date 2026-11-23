using System.Globalization;

namespace AuthApi.Common;

/// <summary>Reports and filters use Vietnam local days (UTC+7); everything is stored in UTC.</summary>
internal static class VnTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTime ToLocal(DateTime utc) => utc + Offset;

    public static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow + Offset);

    public static DateTime StartOfDayUtc(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    /// <summary>
    /// Parses optional yyyy-MM-dd from/to (inclusive days). Defaults to the last
    /// <paramref name="defaultDays"/> days ending today.
    /// </summary>
    public static bool TryParseRange(
        string? from, string? to, int defaultDays, int maxDays,
        out DateOnly fromDay, out DateOnly toDay, out string? error)
    {
        error = null;
        toDay = Today();
        fromDay = toDay.AddDays(-(defaultDays - 1));
        if (!string.IsNullOrWhiteSpace(to) && !TryDay(to, out toDay)) error = "to must be a date like 2026-11-23.";
        if (!string.IsNullOrWhiteSpace(from) && !TryDay(from, out fromDay)) error = "from must be a date like 2026-11-23.";
        if (error is null && string.IsNullOrWhiteSpace(from)) fromDay = toDay.AddDays(-(defaultDays - 1));
        if (error is null && fromDay > toDay) error = "from must not be after to.";
        if (error is null && toDay.DayNumber - fromDay.DayNumber + 1 > maxDays)
            error = $"The range must not exceed {maxDays} days.";
        return error is null;
    }

    public static bool TryDay(string value, out DateOnly day) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    public static string Key(DateTime utc, bool weekly)
    {
        var day = DateOnly.FromDateTime(ToLocal(utc));
        if (weekly) day = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static readonly string[] GroupBys = ["day", "week", "month", "quarter", "year"];

    /// <summary>Longest range a report may cover for a grouping (finer groupings are capped lower).</summary>
    public static int MaxDaysFor(string groupBy) => groupBy switch
    {
        "day" => 366,
        "week" => 731,
        _ => 1827
    };

    /// <summary>A reporting period (day, ISO week, month, quarter or year) in Vietnam local time.</summary>
    public readonly record struct Period(string Key, string Label, DateOnly Start, DateOnly End);

    public static Period PeriodOf(DateOnly day, string groupBy)
    {
        var inv = CultureInfo.InvariantCulture;
        switch (groupBy)
        {
            case "week":
                var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
                var sunday = monday.AddDays(6);
                var asDate = monday.ToDateTime(TimeOnly.MinValue);
                var week = ISOWeek.GetWeekOfYear(asDate);
                var year = ISOWeek.GetYear(asDate);
                return new Period($"{year}-W{week:00}",
                    $"Tuần {week}/{year} ({monday:dd/MM} – {sunday:dd/MM})", monday, sunday);
            case "month":
                var first = new DateOnly(day.Year, day.Month, 1);
                return new Period(first.ToString("yyyy-MM", inv), $"Tháng {day.Month}/{day.Year}",
                    first, first.AddMonths(1).AddDays(-1));
            case "quarter":
                var quarter = (day.Month - 1) / 3 + 1;
                var start = new DateOnly(day.Year, (quarter - 1) * 3 + 1, 1);
                return new Period($"{day.Year}-Q{quarter}", $"Quý {quarter}/{day.Year}", start, start.AddMonths(3).AddDays(-1));
            case "year":
                var jan = new DateOnly(day.Year, 1, 1);
                return new Period(day.Year.ToString(inv), $"Năm {day.Year}", jan, new DateOnly(day.Year, 12, 31));
            default:
                return new Period(day.ToString("yyyy-MM-dd", inv), day.ToString("dd/MM/yyyy", inv), day, day);
        }
    }

    public static Period PeriodOf(DateTime utc, string groupBy) =>
        PeriodOf(DateOnly.FromDateTime(ToLocal(utc)), groupBy);
}
