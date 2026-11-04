using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tasklog.Api.Common;
using Tasklog.Api.Data;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Activity;

public sealed record ActivityDto(
    long Id,
    DateTime Timestamp,
    ActivityEntityType EntityType,
    Guid EntityId,
    Guid? TaskId,
    string Title,
    string? ParentTitle,
    ActivityAction Action,
    IReadOnlyList<FieldChange> Changes);

public sealed record ActivityPageDto(IReadOnlyList<ActivityDto> Items, long? NextCursor);

public sealed record ActivitySummaryDto(int Created, int Completed, int Updated, int Deleted, IReadOnlyList<string> CompletedTitles);

public static class ActivityEndpoints
{
    public static RouteGroupBuilder MapActivityEndpoints(this RouteGroupBuilder api)
    {
        var activity = api.MapGroup("/activity").WithTags("Activity");

        activity.MapGet("/", GetPage);
        activity.MapGet("/summary", GetSummary);
        activity.MapGet("/export", ExportCsv);

        return api;
    }

    /// <summary>
    /// Newest first, cursor-paginated: pass the previous page's nextCursor as
    /// ?cursor= to get older entries. Ids only grow, so "Id &lt; cursor" is a
    /// stable cursor even while new entries are being written.
    /// </summary>
    private static async Task<IResult> GetPage(
        AppDbContext db,
        Guid? taskId,
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? cursor,
        int? limit,
        CancellationToken ct)
    {
        var query = Filter(db, taskId, from, to);

        if (!string.IsNullOrWhiteSpace(action))
        {
            if (!Enum.TryParse<ActivityAction>(action, ignoreCase: true, out var a))
                return new Validation().Require(false, "action", "Yanlış əməliyyat növü.").ToResult();
            query = query.Where(e => e.Action == a);
        }

        if (cursor is { } c) query = query.Where(e => e.Id < c);

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var rows = await query.OrderByDescending(e => e.Id).Take(take + 1).ToListAsync(ct);

        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return Results.Ok(new ActivityPageDto(
            rows.Select(ToDto).ToList(),
            hasMore ? rows[^1].Id : (long?)null));
    }

    /// <summary>Counts for the journal's "from the history" box (tasks only).</summary>
    private static async Task<IResult> GetSummary(
        AppDbContext db, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await Filter(db, null, from, to)
            .Where(e => e.EntityType == ActivityEntityType.Task)
            .OrderBy(e => e.Id)
            .Select(e => new { e.Action, e.Title })
            .ToListAsync(ct);

        return Results.Ok(new ActivitySummaryDto(
            Created: rows.Count(r => r.Action == ActivityAction.Created),
            Completed: rows.Count(r => r.Action == ActivityAction.Completed),
            Updated: rows.Count(r => r.Action == ActivityAction.Updated),
            Deleted: rows.Count(r => r.Action == ActivityAction.Deleted),
            CompletedTitles: rows.Where(r => r.Action == ActivityAction.Completed).Select(r => r.Title).ToList()));
    }

    /// <summary>
    /// CSV in UTF-8 with BOM so Excel shows Azerbaijani letters correctly.
    /// Times are UTC (ISO 8601); a spreadsheet can convert them.
    /// </summary>
    private static async Task<IResult> ExportCsv(
        AppDbContext db, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var rows = await Filter(db, null, from, to).OrderBy(e => e.Id).ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("timestamp_utc,entity,action,title,parent_task,changes");

        foreach (var row in rows)
        {
            var changes = string.Join("; ", Changes(row).Select(c => $"{c.Field}: {c.From ?? "—"} → {c.To ?? "—"}"));
            csv.AppendLine(string.Join(",",
                row.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                row.EntityType.ToString(),
                row.Action.ToString(),
                Escape(row.Title),
                Escape(row.ParentTitle),
                Escape(changes)));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", "tasklog-tarixce.csv");
    }

    private static IQueryable<ActivityEntry> Filter(AppDbContext db, Guid? taskId, DateTimeOffset? from, DateTimeOffset? to)
    {
        IQueryable<ActivityEntry> query = db.ActivityEntries.AsNoTracking();

        if (taskId is { } id) query = query.Where(e => e.TaskId == id);

        if (from is { } f)
        {
            var fromUtc = f.UtcDateTime;
            query = query.Where(e => e.Timestamp >= fromUtc);
        }

        if (to is { } t)
        {
            var toUtc = t.UtcDateTime;
            query = query.Where(e => e.Timestamp < toUtc);
        }

        return query;
    }

    private static ActivityDto ToDto(ActivityEntry e) => new(
        e.Id, e.Timestamp, e.EntityType, e.EntityId, e.TaskId, e.Title, e.ParentTitle, e.Action, Changes(e));

    private static IReadOnlyList<FieldChange> Changes(ActivityEntry e)
    {
        if (e.ChangesJson is null) return Array.Empty<FieldChange>();
        return JsonSerializer.Deserialize<List<FieldChange>>(e.ChangesJson) ?? new List<FieldChange>();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
