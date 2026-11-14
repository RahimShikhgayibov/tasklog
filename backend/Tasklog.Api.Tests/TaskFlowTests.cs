using System.Net;
using System.Net.Http.Json;
using Tasklog.Api.Domain;
using Tasklog.Api.Features.Activity;
using Tasklog.Api.Features.Categories;
using Tasklog.Api.Features.Journal;
using Tasklog.Api.Features.Tasks;

namespace Tasklog.Api.Tests;

public sealed class TaskFlowTests(TasklogApp app) : IClassFixture<TasklogApp>
{
    private static Task<ActivityPageDto> History(HttpClient client, Guid taskId) =>
        client.GetAsync<ActivityPageDto>($"/api/activity?taskId={taskId}");

    [Fact]
    public async Task Every_change_to_a_task_lands_in_the_history()
    {
        var client = await app.LoggedInAsync();

        var task = await client.PostAsync<TaskDto>("/api/tasks", new { title = "Hesabat hazırla", priority = "Medium" });

        await client.PutAsync<TaskDto>($"/api/tasks/{task.Id}", new
        {
            title = task.Title,
            priority = "High",
            dueAt = (DateTime?)null,
            categoryId = (Guid?)null,
            tags = Array.Empty<string>(),
            recurrence = "None"
        });
        await client.PostAsync<CompleteTaskResponse>($"/api/tasks/{task.Id}/complete");

        var delete = await client.DeleteAsync($"/api/tasks/{task.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var history = await History(client, task.Id);

        // Newest first.
        Assert.Equal(
            new[] { ActivityAction.Deleted, ActivityAction.Completed, ActivityAction.Updated, ActivityAction.Created },
            history.Items.Select(e => e.Action).ToArray());

        var update = history.Items.Single(e => e.Action == ActivityAction.Updated);
        var change = Assert.Single(update.Changes);
        Assert.Equal(new FieldChange("Priority", "Medium", "High"), change);

        // History outlives the task.
        Assert.All(history.Items, e => Assert.Equal("Hesabat hazırla", e.Title));
    }

    [Fact]
    public async Task Saving_without_changes_writes_no_history()
    {
        var client = await app.LoggedInAsync();
        var task = await client.PostAsync<TaskDto>("/api/tasks", new { title = "Dəyişməyən", tags = new[] { "a" } });

        await client.PutAsync<TaskDto>($"/api/tasks/{task.Id}", new
        {
            title = task.Title,
            priority = task.Priority,
            dueAt = task.DueAt,
            categoryId = task.CategoryId,
            tags = task.Tags,
            recurrence = task.Recurrence
        });

        var history = await History(client, task.Id);
        Assert.Equal(new[] { ActivityAction.Created }, history.Items.Select(e => e.Action).ToArray());
    }

    [Fact]
    public async Task Subtasks_are_logged_with_their_parent_but_not_when_created_with_the_task()
    {
        var client = await app.LoggedInAsync();
        var task = await client.PostAsync<TaskDto>("/api/tasks", new
        {
            title = "Order API",
            subTasks = new[] { "Kontrakt", "Testlər" }
        });
        Assert.Equal(new[] { "Kontrakt", "Testlər" }, task.SubTasks.Select(s => s.Title).ToArray());

        var withThird = await client.PostAsync<TaskDto>($"/api/tasks/{task.Id}/subtasks", new { title = "Deploy" });
        var deploy = withThird.SubTasks.Single(s => s.Title == "Deploy");
        Assert.Equal(2, deploy.Order);

        var afterDone = await client.PatchAsync<TaskDto>($"/api/tasks/{task.Id}/subtasks/{deploy.Id}", new { isDone = true });
        Assert.True(afterDone.SubTasks.Single(s => s.Id == deploy.Id).IsDone);

        var history = await History(client, task.Id);
        Assert.Equal(
            new[]
            {
                (ActivityEntityType.SubTask, ActivityAction.Completed),
                (ActivityEntityType.SubTask, ActivityAction.Created),
                (ActivityEntityType.Task, ActivityAction.Created)
            },
            history.Items.Select(e => (e.EntityType, e.Action)).ToArray());
        Assert.All(history.Items.Where(e => e.EntityType == ActivityEntityType.SubTask),
            e => Assert.Equal("Order API", e.ParentTitle));
    }

    [Fact]
    public async Task Completing_a_daily_task_creates_tomorrows_occurrence()
    {
        var client = await app.LoggedInAsync();
        var due = app.TodayUtc.AddHours(18); // 22:00 in Baku, still today

        var task = await client.PostAsync<TaskDto>("/api/tasks", new
        {
            title = "İngilis dili — 20 söz",
            dueAt = due,
            recurrence = "Daily",
            subTasks = new[] { "Oxu", "Təkrarla" }
        });

        var result = await client.PostAsync<CompleteTaskResponse>($"/api/tasks/{task.Id}/complete");

        Assert.True(result.Task.IsCompleted);
        var next = Assert.IsType<TaskDto>(result.NextOccurrence);
        Assert.False(next.IsCompleted);
        Assert.Equal(task.SeriesId, next.SeriesId);
        Assert.Equal(due.AddDays(1), next.DueAt);
        Assert.Equal(new[] { "Oxu", "Təkrarla" }, next.SubTasks.Select(s => s.Title).ToArray());
        Assert.All(next.SubTasks, s => Assert.False(s.IsDone));
    }

    [Fact]
    public async Task Search_is_case_insensitive_and_tags_are_normalized()
    {
        var client = await app.LoggedInAsync();
        var task = await client.PostAsync<TaskDto>("/api/tasks", new
        {
            title = "Pagination ENDPOINT yaz",
            tags = new[] { " #Backend", "backend", "dotnet" }
        });
        Assert.Equal(new[] { "Backend", "dotnet" }, task.Tags.ToArray());

        var bySearch = await client.GetAsync<List<TaskDto>>("/api/tasks?search=endpoint");
        Assert.Contains(bySearch, t => t.Id == task.Id);

        var byTag = await client.GetAsync<List<TaskDto>>("/api/tasks?tag=Backend");
        Assert.Contains(byTag, t => t.Id == task.Id);

        // "%" is matched literally, not as a wildcard.
        var percent = await client.GetAsync<List<TaskDto>>("/api/tasks?search=%25");
        Assert.DoesNotContain(percent, t => t.Id == task.Id);
    }

    [Fact]
    public async Task Deleting_a_category_keeps_its_tasks()
    {
        var client = await app.LoggedInAsync();
        var category = await client.PostAsync<CategoryDto>("/api/categories", new { name = "Müvəqqəti", color = "#123456" });
        var task = await client.PostAsync<TaskDto>("/api/tasks", new { title = "Kateqoriyalı", categoryId = category.Id });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);

        var reloaded = await client.GetAsync<TaskDto>($"/api/tasks/{task.Id}");
        Assert.Null(reloaded.CategoryId);
    }

    [Fact]
    public async Task Default_categories_are_seeded()
    {
        var client = await app.LoggedInAsync();
        var categories = await client.GetAsync<List<CategoryDto>>("/api/categories");
        Assert.Contains(categories, c => c.Name == "İş");
        Assert.Contains(categories, c => c.Name == "Layihələr");
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_400()
    {
        var client = await app.LoggedInAsync();
        var emptyTitle = await client.PostAsJsonAsync("/api/tasks", new { title = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, emptyTitle.StatusCode);

        var badColor = await client.PostAsJsonAsync("/api/categories", new { name = "X", color = "blue" });
        Assert.Equal(HttpStatusCode.BadRequest, badColor.StatusCode);
    }

    [Fact]
    public async Task Journal_has_one_entry_per_day()
    {
        var client = await app.LoggedInAsync();
        var day = DateOnly.FromDateTime(app.TodayUtc).ToString("yyyy-MM-dd");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/journal/{day}")).StatusCode);

        await client.PutAsync<JournalDto>($"/api/journal/{day}", new { done = "Birinci", minutesSpent = 90 });
        var saved = await client.PutAsync<JournalDto>($"/api/journal/{day}", new { done = "İkinci", learned = "EF", minutesSpent = 120 });

        Assert.Equal("İkinci", saved.Done);
        Assert.Equal("EF", saved.Learned);
        Assert.Equal(120, saved.MinutesSpent);

        var list = await client.GetAsync<List<JournalDto>>("/api/journal");
        Assert.Single(list, j => j.Date == DateOnly.FromDateTime(app.TodayUtc));
    }
}
