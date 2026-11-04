using Microsoft.EntityFrameworkCore;
using Tasklog.Api.Common;
using Tasklog.Api.Data;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Stats;

public sealed record DayStatDto(DateOnly Date, int Created, int Completed);

public sealed record GroupStatDto(string Key, string Name, string? Color, int Total, int Completed);

public sealed record HabitDayDto(DateOnly Date, bool Done);

public sealed record HabitDto(
    Guid SeriesId,
    string Title,
    Recurrence Recurrence,
    IReadOnlyList<HabitDayDto> Days,
    int? CurrentStreak);

public sealed record BestStreakDto(string Title, int Days);

public sealed record StatsDto(
    DateOnly From,
    DateOnly To,
    int CreatedCount,
    int CompletedCount,
    int PreviousCompletedCount,
    double? CompletionRate,
    int OverdueCount,
    BestStreakDto? BestStreak,
    IReadOnlyList<DayStatDto> PerDay,
    IReadOnlyList<GroupStatDto> ByCategory,
    IReadOnlyList<GroupStatDto> ByPriority,
    IReadOnlyList<HabitDto> Habits);

public static class StatsEndpoints
{
    public static RouteGroupBuilder MapStatsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stats", GetStats).WithTags("Stats");
        return api;
    }

    /// <summary>
    /// GET /api/stats?from=2026-10-29&amp;to=2026-11-04 (inclusive local dates,
    /// time zone from the X-Time-Zone header).
    ///
    /// This is a personal app with small data, so rows are loaded and grouped
    /// in memory — simpler and easier to read than SQL date bucketing across
    /// time zones. Revisit if the data ever gets large.
    /// </summary>
    private static async Task<IResult> GetStats(
        DateOnly from, DateOnly to, HttpContext http, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(from <= to, "from", "'from' 'to'-dan böyük ola bilməz.")
            .Require(to.DayNumber - from.DayNumber <= 366, "to", "Dövr ən çox 1 il ola bilər.");
        if (!validation.IsValid) return validation.ToResult();

        var tz = TimeZoneResolver.Resolve(http);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var today = TimeZoneResolver.LocalDate(nowUtc, tz);

        var fromUtc = TimeZoneResolver.StartOfDayUtc(from, tz);
        var toUtc = TimeZoneResolver.StartOfDayUtc(to.AddDays(1), tz);

        var length = to.DayNumber - from.DayNumber + 1;
        var prevFromUtc = TimeZoneResolver.StartOfDayUtc(from.AddDays(-length), tz);

        // --- History: created / completed per local day --------------------
        var logs = await db.ActivityEntries.AsNoTracking()
            .Where(e => e.EntityType == ActivityEntityType.Task &&
                        (e.Action == ActivityAction.Created || e.Action == ActivityAction.Completed) &&
                        e.Timestamp >= prevFromUtc && e.Timestamp < toUtc)
            .Select(e => new { e.Action, e.Timestamp })
            .ToListAsync(ct);

        var inRange = logs.Where(l => l.Timestamp >= fromUtc).ToList();
        var previousCompleted = logs.Count(l => l.Timestamp < fromUtc && l.Action == ActivityAction.Completed);

        var perDayLookup = inRange
            .GroupBy(l => TimeZoneResolver.LocalDate(l.Timestamp, tz))
            .ToDictionary(
                g => g.Key,
                g => (Created: g.Count(x => x.Action == ActivityAction.Created),
                      Completed: g.Count(x => x.Action == ActivityAction.Completed)));

        var perDay = Enumerable.Range(0, length)
            .Select(i => from.AddDays(i))
            .Select(d => perDayLookup.TryGetValue(d, out var v)
                ? new DayStatDto(d, v.Created, v.Completed)
                : new DayStatDto(d, 0, 0))
            .ToList();

        // --- Tasks created in the period: by category / priority -----------
        var created = await db.Tasks.AsNoTracking()
            .Where(t => t.CreatedAt >= fromUtc && t.CreatedAt < toUtc)
            .Select(t => new { t.CategoryId, t.Priority, t.IsCompleted })
            .ToListAsync(ct);

        var categories = await db.Categories.AsNoTracking()
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var byCategory = categories
            .Select(c => new GroupStatDto(
                c.Id.ToString(), c.Name, c.Color,
                created.Count(t => t.CategoryId == c.Id),
                created.Count(t => t.CategoryId == c.Id && t.IsCompleted)))
            .Where(g => g.Total > 0)
            .ToList();

        var uncategorized = created.Where(t => t.CategoryId == null).ToList();
        if (uncategorized.Count > 0)
            byCategory.Add(new GroupStatDto("none", "Kateqoriyasız", null, uncategorized.Count, uncategorized.Count(t => t.IsCompleted)));

        var byPriority = new[] { Priority.High, Priority.Medium, Priority.Low }
            .Select(p => new GroupStatDto(
                p.ToString(), p.ToString(), null,
                created.Count(t => t.Priority == p),
                created.Count(t => t.Priority == p && t.IsCompleted)))
            .ToList();

        var overdue = await db.Tasks.CountAsync(t => !t.IsCompleted && t.DueAt != null && t.DueAt < nowUtc, ct);

        // --- Habits (recurring series) -------------------------------------
        var habits = await BuildHabits(db, tz, from, to, today, nowUtc, ct);
        var best = habits
            .Where(h => h.CurrentStreak is > 0)
            .OrderByDescending(h => h.CurrentStreak)
            .Select(h => new BestStreakDto(h.Title, h.CurrentStreak!.Value))
            .FirstOrDefault();

        return Results.Ok(new StatsDto(
            from, to,
            CreatedCount: created.Count,
            CompletedCount: inRange.Count(l => l.Action == ActivityAction.Completed),
            PreviousCompletedCount: previousCompleted,
            CompletionRate: created.Count == 0 ? (double?)null : Math.Round((double)created.Count(t => t.IsCompleted) / created.Count, 3),
            OverdueCount: overdue,
            BestStreak: best,
            PerDay: perDay,
            ByCategory: byCategory,
            ByPriority: byPriority,
            Habits: habits));
    }

    private static async Task<List<HabitDto>> BuildHabits(
        AppDbContext db, TimeZoneInfo tz, DateOnly from, DateOnly to, DateOnly today, DateTime nowUtc, CancellationToken ct)
    {
        var occurrences = await db.Tasks.AsNoTracking()
            .Where(t => t.Recurrence != Recurrence.None)
            .Select(t => new { t.Id, t.SeriesId, t.Title, t.Recurrence, t.IsCompleted, t.CreatedAt })
            .ToListAsync(ct);

        if (occurrences.Count == 0) return [];

        var seriesByTask = occurrences.ToDictionary(o => o.Id, o => o.SeriesId);

        // Look back far enough to measure streaks that started before the range.
        var since = nowUtc.AddDays(-400);
        var completions = await db.ActivityEntries.AsNoTracking()
            .Where(e => e.EntityType == ActivityEntityType.Task &&
                        e.Action == ActivityAction.Completed &&
                        e.Timestamp >= since)
            .Select(e => new { e.TaskId, e.Timestamp })
            .ToListAsync(ct);

        var doneDaysBySeries = completions
            .Where(c => c.TaskId != null && seriesByTask.ContainsKey(c.TaskId.Value))
            .GroupBy(c => seriesByTask[c.TaskId!.Value])
            .ToDictionary(g => g.Key, g => g.Select(c => TimeZoneResolver.LocalDate(c.Timestamp, tz)).ToHashSet());

        var habits = new List<HabitDto>();
        foreach (var series in occurrences.GroupBy(o => o.SeriesId))
        {
            // The newest occurrence carries the current title and rule.
            var latest = series.OrderByDescending(o => o.CreatedAt).First();
            var doneDays = doneDaysBySeries.GetValueOrDefault(series.Key) ?? new HashSet<DateOnly>();

            var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
                .Select(i => from.AddDays(i))
                .Select(d => new HabitDayDto(d, doneDays.Contains(d)))
                .ToList();

            habits.Add(new HabitDto(series.Key, latest.Title, latest.Recurrence, days,
                CurrentStreak(doneDays, latest.Recurrence, today)));
        }

        return habits.OrderBy(h => h.Title).ToList();
    }

    /// <summary>
    /// Consecutive scheduled days completed, counting back from today. Today
    /// not done yet does not break the streak (the day isn't over).
    /// Only meaningful for daily rules; weekly/monthly return null.
    /// </summary>
    private static int? CurrentStreak(HashSet<DateOnly> doneDays, Recurrence rule, DateOnly today)
    {
        if (rule is not (Recurrence.Daily or Recurrence.Weekdays)) return null;

        bool IsScheduled(DateOnly d) =>
            rule == Recurrence.Daily || d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        var day = today;
        if (!doneDays.Contains(day)) day = day.AddDays(-1);

        var streak = 0;
        for (var guard = 0; guard < 400; guard++, day = day.AddDays(-1))
        {
            if (!IsScheduled(day)) continue;
            if (!doneDays.Contains(day)) break;
            streak++;
        }

        return streak;
    }
}
