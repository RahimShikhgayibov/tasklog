using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Tasks;

public sealed record SubTaskDto(Guid Id, string Title, bool IsDone, int Order);

public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Description,
    Priority Priority,
    DateTime? DueAt,
    bool IsCompleted,
    DateTime? CompletedAt,
    Guid? CategoryId,
    IReadOnlyList<string> Tags,
    Recurrence Recurrence,
    Guid SeriesId,
    IReadOnlyList<SubTaskDto> SubTasks,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    Priority? Priority,
    DateTime? DueAt,
    Guid? CategoryId,
    List<string>? Tags,
    Recurrence? Recurrence,
    List<string>? SubTasks);

/// <summary>Full replace of the editable fields. Completion has its own endpoints.</summary>
public sealed record UpdateTaskRequest(
    string Title,
    string? Description,
    Priority Priority,
    DateTime? DueAt,
    Guid? CategoryId,
    List<string>? Tags,
    Recurrence Recurrence);

public sealed record CompleteTaskResponse(TaskDto Task, TaskDto? NextOccurrence);

public sealed record CreateSubTaskRequest(string Title);

public sealed record UpdateSubTaskRequest(string? Title, bool? IsDone, int? Order);

public sealed record TaskCountsDto(
    int Today,
    int Overdue,
    int Upcoming,
    int AllOpen,
    IReadOnlyDictionary<Guid, int> ByCategory);

public static class TaskMapping
{
    public static TaskDto ToDto(this TaskItem t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.Priority,
        t.DueAt,
        t.IsCompleted,
        t.CompletedAt,
        t.CategoryId,
        t.Tags,
        t.Recurrence,
        t.SeriesId,
        t.SubTasks.OrderBy(s => s.Order).Select(s => new SubTaskDto(s.Id, s.Title, s.IsDone, s.Order)).ToList(),
        t.CreatedAt,
        t.UpdatedAt);

    /// <summary>" #backend " → "backend"; drops empties and duplicates (case-insensitive).</summary>
    public static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? Enumerable.Empty<string>())
            .Select(t => t.Trim().TrimStart('#').Trim())
            .Where(t => t.Length is > 0 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();
}
