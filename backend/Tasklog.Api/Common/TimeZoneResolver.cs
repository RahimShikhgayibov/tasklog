namespace Tasklog.Api.Common;

/// <summary>
/// The browser sends its IANA time zone (e.g. "Asia/Baku") in a header.
/// The server needs it for anything that depends on the user's calendar day:
/// recurrence ("tomorrow"), per-day stats and habit streaks.
/// </summary>
public static class TimeZoneResolver
{
    public const string HeaderName = "X-Time-Zone";

    public static TimeZoneInfo Resolve(HttpContext http)
    {
        var id = http.Request.Headers[HeaderName].ToString();
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz))
            return tz;

        return TimeZoneInfo.Utc;
    }

    /// <summary>The user's local calendar day for a UTC instant.</summary>
    public static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz));

    /// <summary>UTC instant of local midnight at the start of <paramref name="date"/>.</summary>
    public static DateTime StartOfDayUtc(DateOnly date, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), tz);
}
