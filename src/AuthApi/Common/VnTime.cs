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
}
