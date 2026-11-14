using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Tasklog.Api.Data;
using Tasklog.Api.Features.Activity;
using Tasklog.Api.Features.Auth;
using Tasklog.Api.Features.Categories;
using Tasklog.Api.Features.Journal;
using Tasklog.Api.Features.Stats;
using Tasklog.Api.Features.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Cloud hosts (Render, Koyeb…) tell the app which port to listen on.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Configuration is read lazily (inside the service factories below), so the
// test project can override it through WebApplicationFactory.

// ---------------------------------------------------------------- database
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ActivityLogInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config.GetConnectionString("Default")
                           ?? config["DATABASE_URL"]
                           ?? throw new InvalidOperationException(
                               "Database connection string is missing. Set ConnectionStrings__Default " +
                               "(or DATABASE_URL) — see README.");

    options
        .UseNpgsql(PostgresConnectionString.Normalize(connectionString),
            npgsql => npgsql.EnableRetryOnFailure(3)) // the free DB sleeps; reconnects can blip
        .AddInterceptors(sp.GetRequiredService<ActivityLogInterceptor>());
});

// ---------------------------------------------------------------- login
builder.Services.AddSingleton(sp => PasswordAuth.FromConfiguration(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IHostEnvironment>()));

builder.Services.AddDataProtection()
    .SetApplicationName("Tasklog")
    .PersistKeysToDbContext<AppDbContext>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "tasklog.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;

        // An API answers 401/403; it doesn't redirect to a login page.
        o.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            },
            OnValidatePrincipal = AuthEndpoints.ValidatePrincipal
        };
    });

// In the cloud the site is HTTPS, so the cookie is HTTPS-only there.
// Locally (http://localhost) and in tests it can't be.
builder.Services
    .AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<IHostEnvironment>((o, env) =>
        o.Cookie.SecurePolicy = env.IsProduction() ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest);

builder.Services.AddAuthorization();

// Slows down password guessing: at most 10 login attempts per minute.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter(AuthEndpoints.LoginRateLimit, w =>
    {
        w.PermitLimit = 10;
        w.Window = TimeSpan.FromMinutes(1);
        w.QueueLimit = 0;
    });
});

// ---------------------------------------------------------------- API
// Enums as "High" instead of 2 in JSON.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

// Fail at startup, not at the first login, if the password isn't configured.
app.Services.GetRequiredService<PasswordAuth>();

// Outside production, show the exception details (makes failing tests readable).
if (app.Environment.IsProduction()) app.UseExceptionHandler();
else app.UseDeveloperExceptionPage();
app.UseStatusCodePages();

// The built React app lives in wwwroot (copied there by the Dockerfile).
// index.html is never cached, so a new deploy shows up immediately; the
// hashed files under /assets can be cached forever.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";

var staticFiles = new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        if (ctx.File.Name == "index.html")
            headers[HeaderNames.CacheControl] = "no-cache";
        else if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
            headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
    }
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

// Apply migrations and seed default categories on startup.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (!db.Database.GetMigrations().Any())
        throw new InvalidOperationException(
            "No EF Core migrations found. Run once: dotnet ef migrations add InitialCreate");
    await db.Database.MigrateAsync();
    await CategoryEndpoints.SeedDefaultsAsync(db);
}

// Everything under /api needs a login, except the login endpoint itself.
var api = app.MapGroup("/api").RequireAuthorization();
api.MapAuthEndpoints();
api.MapTaskEndpoints();
api.MapCategoryEndpoints();
api.MapActivityEndpoints();
api.MapJournalEndpoints();
api.MapStatsEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Unknown /api/... → 404; any other path → the React app (client-side routing).
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

// Lets the test project use WebApplicationFactory<Program>.
public partial class Program { }
