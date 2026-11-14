using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Tasks;

/// <summary>
/// When a recurring task is completed, the next occurrence is created as a new
/// task. This decides its deadline. Calculation happens in the user's local
/// time so "every day at 09:00" stays at 09:00 local.
/// </summary>
public static class RecurrenceCalculator
{
    public static DateTime NextDueUtc(DateTime? currentDueUtc, Recurrence rule, DateTime nowUtc, TimeZoneInfo tz)
    {
        if (rule == Recurrence.None)
            throw new ArgumentException("Task is not recurring.", nameof(rule));

        var nowLocal = ToLocal(nowUtc, tz);
        var next = Step(currentDueUtc is { } due ? ToLocal(due, tz) : nowLocal, rule);

        // Completing an overdue task late must not create an occurrence that is
        // already overdue: skip forward to the first one still in the future.
        while (next <= nowLocal)
            next = Step(next, rule);

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(next, DateTimeKind.Unspecified), tz);
    }

    private static DateTime ToLocal(DateTime utc, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);

    private static DateTime Step(DateTime local, Recurrence rule) => rule switch
    {
        Recurrence.Daily => local.AddDays(1),
        Recurrence.Weekdays => NextWeekday(local),
        Recurrence.Weekly => local.AddDays(7),
        Recurrence.Monthly => local.AddMonths(1),
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null)
    };

    private static DateTime NextWeekday(DateTime local)
    {
        var next = local.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }
}
