using System.Globalization;

namespace HrCore.Infrastructure;

public static class ManilaTime
{
    public const string ZoneId = "Asia/Manila";

    public static TimeZoneInfo Zone { get; } = Resolve();

    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(SpecifyUtc(utc), Zone);

    public static DateTime ToManila(DateTime utc) => ToLocal(utc);

    public static DateOnly ToManilaDate(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    public static DateOnly Today(DateTime? utcNow = null) =>
        DateOnly.FromDateTime(ToLocal(utcNow ?? DateTime.UtcNow));

    public static DateTime StartOfDayUtc(DateOnly localDate) =>
        TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue), Zone);

    /// <summary>
    /// Parse a Manila wall-clock string (datetime-local or "yyyy-MM-dd HH:mm") into UTC.
    /// A value that already carries a Z/offset is taken as that instant.
    /// </summary>
    public static bool TryParseLocalToUtc(string? text, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var raw = text.Trim();
        string[] formats =
        [
            "yyyy-MM-dd'T'HH:mm",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFF",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd h:mm tt",
            "yyyy-MM-dd hh:mm tt"
        ];
        var en = CultureInfo.GetCultureInfo("en-US");
        if (!DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            && !DateTime.TryParseExact(raw, formats, en, DateTimeStyles.None, out local)
            && !DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out local)
            && !DateTime.TryParse(raw, en, DateTimeStyles.RoundtripKind, out local))
        {
            return false;
        }

        if (local.Kind == DateTimeKind.Utc)
        {
            utc = local;
            return true;
        }

        if (local.Kind == DateTimeKind.Local)
        {
            utc = local.ToUniversalTime();
            return true;
        }

        utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);
        return true;
    }

    public static DateTime StartOfTodayUtc(DateTime? utcNow = null) =>
        StartOfDayUtc(Today(utcNow));

    public static string FormatPunchLabel(DateTime utc, DateTime? utcNow = null)
    {
        var local = ToLocal(utc);
        var time = local.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-US"));
        if (DateOnly.FromDateTime(local) == Today(utcNow))
        {
            return time;
        }

        return local.ToString("MMM d, h:mm tt", CultureInfo.GetCultureInfo("en-US"));
    }

    /// <summary>Always includes the Manila calendar date, e.g. 2026-09-17 8:02 AM.</summary>
    public static string FormatRegisterDateTime(DateTime utc) =>
        ToLocal(utc).ToString("yyyy-MM-dd h:mm tt", CultureInfo.GetCultureInfo("en-US"));

    public static string FormatDate(DateTime utc) =>
        ToLocal(utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var diff = ((int)date.DayOfWeek - (int)weekStart + 7) % 7;
        return date.AddDays(-diff);
    }

    public static DateOnly EndOfWeek(DateOnly date, DayOfWeek weekStart = DayOfWeek.Monday) =>
        StartOfWeek(date, weekStart).AddDays(6);

    public static (DateOnly From, DateOnly To) CurrentWeek(DateTime? utcNow = null, DayOfWeek weekStart = DayOfWeek.Monday)
    {
        var today = Today(utcNow);
        return (StartOfWeek(today, weekStart), EndOfWeek(today, weekStart));
    }

    private static DateTime SpecifyUtc(DateTime utc) =>
        utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            ZoneId,
            TimeSpan.FromHours(8),
            ZoneId,
            "Philippine Standard Time");
    }
}
