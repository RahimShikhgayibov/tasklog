using Microsoft.EntityFrameworkCore;
using Tasklog.Api.Common;
using Tasklog.Api.Data;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Tasks;

public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks").WithTags("Tasks");

        tasks.MapGet("/", GetTasks);
        tasks.MapGet("/counts", GetCounts);
        tasks.MapGet("/{id:guid}", GetTask);
        tasks.MapPost("/", CreateTask);
        tasks.MapPut("/{id:guid}", UpdateTask);
        tasks.MapDelete("/{id:guid}", DeleteTask);
        tasks.MapPost("/{id:guid}/complete", CompleteTask);
        tasks.MapPost("/{id:guid}/reopen", ReopenTask);

        tasks.MapPost("/{id:guid}/subtasks", AddSubTask);
        tasks.MapPatch("/{id:guid}/subtasks/{subId:guid}", UpdateSubTask);
        tasks.MapDelete("/{id:guid}/subtasks/{subId:guid}", DeleteSubTask);

        return api;
    }

    /// <summary>
    /// GET /api/tasks?status=open|completed|all&amp;categoryId=&amp;tag=&amp;priority=High
    ///   &amp;search=&amp;dueBefore=&amp;dueAfter=&amp;completedAfter=&amp;limit=
    /// The frontend builds the views (Today, Upcoming…) from these filters,
    /// passing day boundaries already converted to UTC.
    /// </summary>
    private static async Task<IResult> GetTasks(
        AppDbContext db,
        string? status,
        Guid? categoryId,
        string? tag,
        string? priority,
        string? search,
        DateTimeOffset? dueBefore,
        DateTimeOffset? dueAfter,
        DateTimeOffset? completedAfter,
        int? limit,
        CancellationToken ct)
    {
        IQueryable<TaskItem> query = db.Tasks.AsNoTracking().Include(t => t.SubTasks);

        query = (status ?? "open").ToLowerInvariant() switch
        {
            "completed" => query.Where(t => t.IsCompleted),
            "all" => query,
            _ => query.Where(t => !t.IsCompleted)
        };

        if (categoryId is { } cat) query = query.Where(t => t.CategoryId == cat);

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalized = tag.Trim().TrimStart('#');
            query = query.Where(t => t.Tags.Contains(normalized));
        }

        if (!string.IsNullOrWhiteSpace(priority))
        {
            if (!Enum.TryParse<Priority>(priority, ignoreCase: true, out var p))
                return new Validation().Require(false, "priority", "Yanlış prioritet.").ToResult();
            query = query.Where(t => t.Priority == p);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(t => EF.Functions.Like(t.Title, pattern) ||
                                     (t.Description != null && EF.Functions.Like(t.Description, pattern)));
        }

        if (dueBefore is { } before)
        {
            var b = before.UtcDateTime;
            query = query.Where(t => t.DueAt != null && t.DueAt <= b);
        }

        if (dueAfter is { } after)
        {
            var a = after.UtcDateTime;
            query = query.Where(t => t.DueAt != null && t.DueAt > a);
        }

        if (completedAfter is { } completedSince)
        {
            var c = completedSince.UtcDateTime;
            query = query.Where(t => t.CompletedAt != null && t.CompletedAt >= c);
        }

        query = status?.ToLowerInvariant() == "completed"
            ? query.OrderByDescending(t => t.CompletedAt)
            : query.OrderBy(t => t.DueAt == null)          // tasks with a deadline first
                .ThenBy(t => t.DueAt)
                .ThenByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAt);

        var take = Math.Clamp(limit ?? 500, 1, 1000);
        var items = await query.Take(take).ToListAsync(ct);

        return Results.Ok(items.Select(t => t.ToDto()));
    }

    /// <summary>Sidebar badges. todayEnd = end of the user's local day, in UTC.</summary>
    private static async Task<IResult> GetCounts(
        AppDbContext db, TimeProvider clock, DateTimeOffset todayEnd, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var end = todayEnd.UtcDateTime;

        var open = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsCompleted)
            .Select(t => new { t.DueAt, t.CategoryId })
            .ToListAsync(ct);

        var byCategory = open
            .Where(t => t.CategoryId != null)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return Results.Ok(new TaskCountsDto(
            Today: open.Count(t => t.DueAt != null && t.DueAt <= end),
            Overdue: open.Count(t => t.DueAt != null && t.DueAt < now),
            Upcoming: open.Count(t => t.DueAt != null && t.DueAt > end),
            AllOpen: open.Count,
            ByCategory: byCategory));
    }

    private static async Task<IResult> GetTask(Guid id, AppDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.AsNoTracking().Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        return task is null ? Results.NotFound() : Results.Ok(task.ToDto());
    }

    private static async Task<IResult> CreateTask(CreateTaskRequest req, AppDbContext db, CancellationToken ct)
    {
        var validation = Validation.Title(req.Title);
        if (req.CategoryId is { } catId)
            validation.Require(await db.Categories.AnyAsync(c => c.Id == catId, ct), "categoryId", "Kateqoriya tapılmadı.");
        if (!validation.IsValid) return validation.ToResult();

        var task = new TaskItem
        {
            Title = req.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            Priority = req.Priority ?? Priority.Medium,
            DueAt = req.DueAt?.ToUniversalTime(),
            CategoryId = req.CategoryId,
            Tags = TaskMapping.NormalizeTags(req.Tags),
            Recurrence = req.Recurrence ?? Recurrence.None,
            SubTasks = (req.SubTasks ?? Enumerable.Empty<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select((s, i) => new SubTask { Title = s.Trim(), Order = i })
                .ToList()
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/tasks/{task.Id}", task.ToDto());
    }

    private static async Task<IResult> UpdateTask(Guid id, UpdateTaskRequest req, AppDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Results.NotFound();

        var validation = Validation.Title(req.Title);
        if (req.CategoryId is { } catId)
            validation.Require(await db.Categories.AnyAsync(c => c.Id == catId, ct), "categoryId", "Kateqoriya tapılmadı.");
        if (!validation.IsValid) return validation.ToResult();

        task.Title = req.Title.Trim();
        task.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        task.Priority = req.Priority;
        task.DueAt = req.DueAt?.ToUniversalTime();
        task.CategoryId = req.CategoryId;
        task.Recurrence = req.Recurrence;

        // Only replace the list when it actually changed, so the history does
        // not get an empty "tags changed" line.
        var tags = TaskMapping.NormalizeTags(req.Tags);
        if (!tags.SequenceEqual(task.Tags)) task.Tags = tags;

        await db.SaveChangesAsync(ct); // the interceptor writes the diff to the history
        return Results.Ok(task.ToDto());
    }

    private static async Task<IResult> DeleteTask(Guid id, AppDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Results.NotFound();

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CompleteTask(
        Guid id, AppDbContext db, TimeProvider clock, HttpContext http, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Results.NotFound();
        if (task.IsCompleted) return Results.Ok(new CompleteTaskResponse(task.ToDto(), null));

        var now = clock.GetUtcNow().UtcDateTime;
        task.IsCompleted = true;
        task.CompletedAt = now;

        TaskItem? next = null;
        if (task.Recurrence != Recurrence.None)
        {
            var tz = TimeZoneResolver.Resolve(http);
            next = new TaskItem
            {
                Title = task.Title,
                Description = task.Description,
                Priority = task.Priority,
                CategoryId = task.CategoryId,
                Tags = [.. task.Tags],
                Recurrence = task.Recurrence,
                SeriesId = task.SeriesId,
                DueAt = RecurrenceCalculator.NextDueUtc(task.DueAt, task.Recurrence, now, tz),
                SubTasks = task.SubTasks
                    .OrderBy(s => s.Order)
                    .Select(s => new SubTask { Title = s.Title, Order = s.Order })
                    .ToList()
            };
            db.Tasks.Add(next);
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new CompleteTaskResponse(task.ToDto(), next?.ToDto()));
    }

    private static async Task<IResult> ReopenTask(Guid id, AppDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Results.NotFound();

        if (task.IsCompleted)
        {
            task.IsCompleted = false;
            task.CompletedAt = null;
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(task.ToDto());
    }

    private static async Task<IResult> AddSubTask(Guid id, CreateSubTaskRequest req, AppDbContext db, CancellationToken ct)
    {
        var validation = Validation.Title(req.Title);
        if (!validation.IsValid) return validation.ToResult();

        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null) return Results.NotFound();

        var order = task.SubTasks.Count == 0 ? 0 : task.SubTasks.Max(s => s.Order) + 1;
        var sub = new SubTask { TaskItemId = task.Id, Title = req.Title.Trim(), Order = order };
        task.SubTasks.Add(sub);

        await db.SaveChangesAsync(ct);
        return Results.Ok(task.ToDto());
    }

    private static async Task<IResult> UpdateSubTask(
        Guid id, Guid subId, UpdateSubTaskRequest req, AppDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        var sub = task?.SubTasks.FirstOrDefault(s => s.Id == subId);
        if (task is null || sub is null) return Results.NotFound();

        if (req.Title is not null)
        {
            var validation = Validation.Title(req.Title);
            if (!validation.IsValid) return validation.ToResult();
            sub.Title = req.Title.Trim();
        }

        if (req.IsDone is { } isDone) sub.IsDone = isDone;
        if (req.Order is { } order) sub.Order = order;

        await db.SaveChangesAsync(ct);
        return Results.Ok(task.ToDto());
    }

    private static async Task<IResult> DeleteSubTask(Guid id, Guid subId, AppDbContext db, CancellationToken ct)
    {
        var task = await db.Tasks.Include(t => t.SubTasks).FirstOrDefaultAsync(t => t.Id == id, ct);
        var sub = task?.SubTasks.FirstOrDefault(s => s.Id == subId);
        if (task is null || sub is null) return Results.NotFound();

        task.SubTasks.Remove(sub);   // required relationship → EF deletes the orphan
        await db.SaveChangesAsync(ct);
        return Results.Ok(task.ToDto());
    }
}
