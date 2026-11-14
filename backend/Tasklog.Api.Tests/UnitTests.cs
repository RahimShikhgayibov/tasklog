using Npgsql;
using Tasklog.Api.Data;
using Tasklog.Api.Domain;
using Tasklog.Api.Features.Tasks;

namespace Tasklog.Api.Tests;

public sealed class RecurrenceCalculatorTests
{
    private static readonly TimeZoneInfo Baku = TimeZoneInfo.FindSystemTimeZoneById("Asia/Baku");

    private static DateTime Utc(int y, int m, int d, int h, int min = 0) => new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Fact]
    public void Daily_keeps_the_local_time()
    {
        // 09:00 in Baku, completed at 10:00 Baku
        var next = RecurrenceCalculator.NextDueUtc(Utc(2026, 11, 4, 5), Recurrence.Daily, Utc(2026, 11, 4, 6), Baku);
        Assert.Equal(Utc(2026, 11, 5, 5), next);
    }

    [Fact]
    public void Weekdays_skip_the_weekend()
    {
        // Friday 6 Nov 2026 → Monday 9 Nov
        var next = RecurrenceCalculator.NextDueUtc(Utc(2026, 11, 6, 5), Recurrence.Weekdays, Utc(2026, 11, 6, 6), Baku);
        Assert.Equal(Utc(2026, 11, 9, 5), next);
        Assert.Equal(DayOfWeek.Monday, next.DayOfWeek);
    }

    [Fact]
    public void Monthly_clamps_to_the_end_of_a_short_month()
    {
        var next = RecurrenceCalculator.NextDueUtc(Utc(2027, 1, 31, 5), Recurrence.Monthly, Utc(2027, 1, 31, 6), Baku);
        Assert.Equal(Utc(2027, 2, 28, 5), next);
    }

    [Fact]
    public void Completing_late_skips_occurrences_already_in_the_past()
    {
        // Due 1 Nov 09:00, completed 4 Nov 14:00 (Baku) → next is 5 Nov 09:00, not 2 Nov.
        var next = RecurrenceCalculator.NextDueUtc(Utc(2026, 11, 1, 5), Recurrence.Daily, Utc(2026, 11, 4, 10), Baku);
        Assert.Equal(Utc(2026, 11, 5, 5), next);
    }

    [Fact]
    public void Without_a_deadline_the_next_one_is_counted_from_now()
    {
        var now = Utc(2026, 11, 4, 10, 30);
        var next = RecurrenceCalculator.NextDueUtc(null, Recurrence.Weekly, now, Baku);
        Assert.Equal(now.AddDays(7), next);
    }
}

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void Converts_a_hosting_url()
    {
        var result = PostgresConnectionString.Normalize(
            "postgresql://rahim:p%40ss%3Aword@ep-cool-name-123.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require");

        var cs = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("ep-cool-name-123.eu-central-1.aws.neon.tech", cs.Host);
        Assert.Equal(5432, cs.Port);
        Assert.Equal("neondb", cs.Database);
        Assert.Equal("rahim", cs.Username);
        Assert.Equal("p@ss:word", cs.Password);
        Assert.Equal(SslMode.Require, cs.SslMode);
    }

    [Fact]
    public void Keeps_a_key_value_connection_string()
    {
        const string value = "Host=localhost;Database=tasklog;Username=a;Password=b";
        Assert.Equal(value, PostgresConnectionString.Normalize(value));
    }
}
