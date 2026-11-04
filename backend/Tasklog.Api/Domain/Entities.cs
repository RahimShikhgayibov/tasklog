namespace Tasklog.Api.Domain;

/// <summary>Entities with CreatedAt/UpdatedAt are stamped automatically on save.</summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

public class TaskItem : IHasTimestamps
{
    public TaskItem()
    {
        // A non-recurring task is its own series. Occurrences of a recurring
        // task share the SeriesId of the first one (used for habit stats).
        SeriesId = Id;
    }

    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Title { get; set; }
    public string? Description { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;

    /// <summary>UTC. Null = no deadline.</summary>
    public DateTime? DueAt { get; set; }

    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    /// <summary>Stored as a JSON column (EF Core primitive collection).</summary>
    public List<string> Tags { get; set; } = [];

    public Recurrence Recurrence { get; set; } = Recurrence.None;
    public Guid SeriesId { get; set; }

    public List<SubTask> SubTasks { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class SubTask
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TaskItemId { get; set; }
    public required string Title { get; set; }
    public bool IsDone { get; set; }
    public int Order { get; set; }
}

public class Category : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }

    /// <summary>Hex color, e.g. #3451D1.</summary>
    public required string Color { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One row of the automatic change history. Written only by
/// <see cref="Data.ActivityLogInterceptor"/>, never by endpoints.
/// No foreign key to the task on purpose: history must survive deletion.
/// </summary>
public class ActivityEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public ActivityEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }

    /// <summary>The task this entry belongs to (the parent task for subtasks).</summary>
    public Guid? TaskId { get; set; }

    /// <summary>Snapshot of the task's (or subtask's) title at the time of the change.</summary>
    public required string Title { get; set; }

    /// <summary>For subtask entries: the parent task's title.</summary>
    public string? ParentTitle { get; set; }

    public ActivityAction Action { get; set; }

    /// <summary>JSON array of <see cref="FieldChange"/>; null when nothing field-level changed.</summary>
    public string? ChangesJson { get; set; }
}

public sealed record FieldChange(string Field, string? From, string? To);

public class JournalEntry : IHasTimestamps
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The user's local calendar day. Unique.</summary>
    public DateOnly Date { get; set; }

    public string? Done { get; set; }
    public string? Learned { get; set; }
    public string? Blockers { get; set; }
    public string? TomorrowPlan { get; set; }
    public int? MinutesSpent { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
