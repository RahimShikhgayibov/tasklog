using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Data;

/// <summary>
/// Writes the change history automatically: just before every SaveChanges it
/// looks at what the change tracker is about to save and turns task and
/// subtask changes into <see cref="ActivityEntry"/> rows in the same
/// transaction. It also stamps CreatedAt/UpdatedAt.
///
/// Endpoints never write to the log themselves, so nothing can be forgotten.
/// Note: ExecuteUpdate/ExecuteDelete bypass the change tracker and therefore
/// this interceptor; don't use them for tasks or subtasks.
/// </summary>
public sealed class ActivityLogInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    // Bookkeeping fields that should not appear as "changes" in the history.
    private static readonly HashSet<string> IgnoredTaskFields =
    [
        nameof(TaskItem.CreatedAt),
        nameof(TaskItem.UpdatedAt),
        nameof(TaskItem.IsCompleted),
        nameof(TaskItem.CompletedAt),
        nameof(TaskItem.SeriesId)
    ];

    // IsDone is handled as Completed/Reopened; reordering alone is not worth a history line.
    private static readonly HashSet<string> IgnoredSubTaskFields =
    [
        nameof(SubTask.IsDone),
        nameof(SubTask.Order)
    ];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is AppDbContext db) Capture(db);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is AppDbContext db) Capture(db);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;

        var entries = db.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        // Subtask noise to skip: subtasks created together with a new task,
        // and subtasks removed because their task is being deleted.
        var addedTaskIds = TaskIds(entries, EntityState.Added);
        var deletedTaskIds = TaskIds(entries, EntityState.Deleted);

        var logs = new List<ActivityEntry>();

        foreach (var entry in entries)
        {
            if (entry.Entity is IHasTimestamps) StampTimestamps(entry, now);

            if (entry.Entity is TaskItem task)
            {
                var log = BuildTaskLog(db, entry, task, now);
                if (log is not null) logs.Add(log);
            }
            else if (entry.Entity is SubTask sub)
            {
                if (entry.State == EntityState.Added && addedTaskIds.Contains(sub.TaskItemId)) continue;
                if (entry.State == EntityState.Deleted && deletedTaskIds.Contains(sub.TaskItemId)) continue;

                var log = BuildSubTaskLog(db, entry, sub, now);
                if (log is not null) logs.Add(log);
            }
        }

        if (logs.Count > 0) db.ActivityEntries.AddRange(logs);
    }

    private static HashSet<Guid> TaskIds(List<EntityEntry> entries, EntityState state) =>
        entries.Where(e => e.State == state && e.Entity is TaskItem)
            .Select(e => ((TaskItem)e.Entity).Id)
            .ToHashSet();

    private static void StampTimestamps(EntityEntry entry, DateTime now)
    {
        if (entry.State == EntityState.Added)
        {
            entry.Property(nameof(IHasTimestamps.CreatedAt)).CurrentValue = now;
            entry.Property(nameof(IHasTimestamps.UpdatedAt)).CurrentValue = now;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Property(nameof(IHasTimestamps.UpdatedAt)).CurrentValue = now;
        }
    }

    private static ActivityEntry? BuildTaskLog(AppDbContext db, EntityEntry entry, TaskItem task, DateTime now)
    {
        ActivityAction action;
        List<FieldChange> changes = [];

        switch (entry.State)
        {
            case EntityState.Added:
                action = ActivityAction.Created;
                break;

            case EntityState.Deleted:
                action = ActivityAction.Deleted;
                break;

            case EntityState.Modified:
                changes = DiffProperties(db, entry, IgnoredTaskFields);

                var completed = entry.Property(nameof(TaskItem.IsCompleted));
                var completionChanged = completed.IsModified &&
                                        !Equals(completed.OriginalValue, completed.CurrentValue);

                if (completionChanged)
                    action = task.IsCompleted ? ActivityAction.Completed : ActivityAction.Reopened;
                else if (changes.Count > 0)
                    action = ActivityAction.Updated;
                else
                    return null; // only bookkeeping fields changed
                break;

            default:
                return null;
        }

        return new ActivityEntry
        {
            Timestamp = now,
            EntityType = ActivityEntityType.Task,
            EntityId = task.Id,
            TaskId = task.Id,
            Title = task.Title,
            Action = action,
            ChangesJson = changes.Count > 0 ? JsonSerializer.Serialize(changes) : null
        };
    }

    private static ActivityEntry? BuildSubTaskLog(AppDbContext db, EntityEntry entry, SubTask sub, DateTime now)
    {
        ActivityAction action;
        List<FieldChange> changes = [];

        switch (entry.State)
        {
            case EntityState.Added:
                action = ActivityAction.Created;
                break;

            case EntityState.Deleted:
                action = ActivityAction.Deleted;
                break;

            case EntityState.Modified:
                var done = entry.Property(nameof(SubTask.IsDone));
                var doneChanged = done.IsModified && !Equals(done.OriginalValue, done.CurrentValue);

                changes = DiffProperties(db, entry, IgnoredSubTaskFields);

                if (doneChanged)
                    action = sub.IsDone ? ActivityAction.Completed : ActivityAction.Reopened;
                else if (changes.Count > 0)
                    action = ActivityAction.Updated;
                else
                    return null;
                break;

            default:
                return null;
        }

        // Find() checks the change tracker first, so this is usually free.
        var parentTitle = db.Tasks.Find(sub.TaskItemId)?.Title;

        return new ActivityEntry
        {
            Timestamp = now,
            EntityType = ActivityEntityType.SubTask,
            EntityId = sub.Id,
            TaskId = sub.TaskItemId,
            Title = sub.Title,
            ParentTitle = parentTitle,
            Action = action,
            ChangesJson = changes.Count > 0 ? JsonSerializer.Serialize(changes) : null
        };
    }

    private static List<FieldChange> DiffProperties(AppDbContext db, EntityEntry entry, IReadOnlySet<string> ignored)
    {
        var changes = new List<FieldChange>();

        foreach (var property in entry.Properties)
        {
            if (!property.IsModified) continue;

            var field = property.Metadata.Name;
            if (ignored.Contains(field)) continue;

            var from = Format(db, field, property.OriginalValue);
            var to = Format(db, field, property.CurrentValue);
            if (from == to) continue;

            changes.Add(new FieldChange(field, from, to));
        }

        return changes;
    }

    /// <summary>
    /// Values are stored as display-neutral strings: enums by name, dates as
    /// ISO 8601 UTC, tags comma-separated, category by name. The frontend
    /// turns them into Azerbaijani labels and local time.
    /// </summary>
    private static string? Format(AppDbContext db, string field, object? value) => value switch
    {
        null => null,
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
        IEnumerable<string> list => string.Join(", ", list),
        Guid id when field == nameof(TaskItem.CategoryId) => db.Categories.Find(id)?.Name ?? id.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
