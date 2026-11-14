using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Tasklog.Api.Data;

namespace Tasklog.Api.Tests;

/// <summary>
/// The real app (all middleware, auth, EF Core, the log interceptor) against a
/// real PostgreSQL. Each test class gets its own throw-away database.
///
/// Locally: docker compose up -d db   (CI provides PostgreSQL itself)
/// Override with the TEST_DATABASE environment variable.
/// </summary>
public sealed class TasklogApp : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "test-password-123";
    public const string TimeZone = "Asia/Baku";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString;

    public TasklogApp()
    {
        var baseConnection = Environment.GetEnvironmentVariable("TEST_DATABASE")
                             ?? "Host=localhost;Port=5432;Username=tasklog;Password=tasklog";
        _connectionString = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = "tasklog_test_" + Guid.NewGuid().ToString("N")[..12]
        }.ConnectionString;

        // Today 09:00 UTC (13:00 in Baku): same calendar day in both zones,
        // and a real date so cookie lifetimes behave normally.
        var today = DateTime.UtcNow.Date;
        Clock = new FakeTimeProvider(new DateTimeOffset(today.AddHours(9), TimeSpan.Zero));
    }

    public FakeTimeProvider Clock { get; }

    /// <summary>Start of "today" in UTC, matching <see cref="Clock"/>.</summary>
    public DateTime TodayUtc => Clock.GetUtcNow().UtcDateTime.Date;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _connectionString,
            ["Auth:Password"] = Password
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>A client that is not logged in.</summary>
    public HttpClient Anonymous()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Time-Zone", TimeZone);
        return client;
    }

    private HttpClient? _loggedIn;

    /// <summary>
    /// A shared logged-in client (tests in one class run one after another).
    /// Sharing keeps the number of logins under the login rate limit.
    /// </summary>
    public async Task<HttpClient> LoggedInAsync() => _loggedIn ??= await NewLoginAsync();

    /// <summary>A fresh client that logs in by itself.</summary>
    public async Task<HttpClient> NewLoginAsync()
    {
        var client = Anonymous();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { password = Password }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        try
        {
            using var scope = Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
        }
        catch
        {
            // Best effort: CI throws the whole database server away anyway.
        }

        await base.DisposeAsync();
    }
}

public static class HttpTestExtensions
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"{(int)response.StatusCode} {response.StatusCode}: {body}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(TasklogApp.Json))!;
    }

    public static async Task<T> GetAsync<T>(this HttpClient client, string url) =>
        await (await client.GetAsync(url)).ReadAsync<T>();

    public static async Task<T> PostAsync<T>(this HttpClient client, string url, object? body = null) =>
        await (await client.PostAsJsonAsync(url, body ?? new { }, TasklogApp.Json)).ReadAsync<T>();

    public static async Task<T> PutAsync<T>(this HttpClient client, string url, object body) =>
        await (await client.PutAsJsonAsync(url, body, TasklogApp.Json)).ReadAsync<T>();

    public static async Task<T> PatchAsync<T>(this HttpClient client, string url, object body) =>
        await (await client.PatchAsJsonAsync(url, body, TasklogApp.Json)).ReadAsync<T>();

    public static string Iso(DateTime utc) => utc.ToString("O");
}
