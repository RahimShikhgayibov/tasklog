using System.Net;
using System.Net.Http.Json;

namespace Tasklog.Api.Tests;

public sealed class AuthTests(TasklogApp app) : IClassFixture<TasklogApp>
{
    [Fact]
    public async Task Health_check_is_public()
    {
        var response = await app.Anonymous().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/tasks")]
    [InlineData("/api/categories")]
    [InlineData("/api/activity")]
    [InlineData("/api/auth/me")]
    public async Task Api_requires_login(string url)
    {
        var response = await app.Anonymous().GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var client = app.Anonymous();

        var login = await client.PostAsJsonAsync("/api/auth/login", new { password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Login_then_logout()
    {
        var client = await app.NewLoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tasks")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_is_404_not_the_react_app()
    {
        var client = await app.LoggedInAsync();
        var response = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
