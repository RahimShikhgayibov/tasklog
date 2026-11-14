using Tasklog.Api.Features.Stats;
using Tasklog.Api.Features.Tasks;

namespace Tasklog.Api.Tests;

// Own class → own database, so the counts are exact.
public sealed class StatsTests(TasklogApp app) : IClassFixture<TasklogApp>
{
    [Fact]
    public async Task Stats_count_todays_work_and_habits()
    {
        var client = await app.LoggedInAsync();

        var a = await client.PostAsync<TaskDto>("/api/tasks", new { title = "A", priority = "High" });
        await client.PostAsync<TaskDto>("/api/tasks", new { title = "B", priority = "Low" });
        var habit = await client.PostAsync<TaskDto>("/api/tasks", new
        {
            title = "Səhər planlaması",
            recurrence = "Daily",
            dueAt = app.TodayUtc.AddHours(18)
        });

        await client.PostAsync<CompleteTaskResponse>($"/api/tasks/{a.Id}/complete");
        await client.PostAsync<CompleteTaskResponse>($"/api/tasks/{habit.Id}/complete");

        var today = DateOnly.FromDateTime(app.TodayUtc);
        var from = today.AddDays(-6).ToString("yyyy-MM-dd");
        var to = today.ToString("yyyy-MM-dd");

        var stats = await client.GetAsync<StatsDto>($"/api/stats?from={from}&to={to}");

        // 3 created by hand + 1 next habit occurrence; 2 completed.
        Assert.Equal(4, stats.CreatedCount);
        Assert.Equal(2, stats.CompletedCount);
        Assert.Equal(7, stats.PerDay.Count);
        Assert.Equal(new DayStatDto(today, 4, 2), stats.PerDay.Last());

        var high = stats.ByPriority.Single(p => p.Key == "High");
        Assert.Equal((1, 1), (high.Total, high.Completed));

        var tracked = Assert.Single(stats.Habits);
        Assert.Equal("Səhər planlaması", tracked.Title);
        Assert.True(tracked.Days.Last().Done);
        Assert.Equal(1, tracked.CurrentStreak);
        Assert.Equal(1, stats.BestStreak?.Days);
    }
}
