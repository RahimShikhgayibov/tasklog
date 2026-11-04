using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Tasklog.Api.Data;
using Tasklog.Api.Features.Activity;
using Tasklog.Api.Features.Categories;
using Tasklog.Api.Features.Journal;
using Tasklog.Api.Features.Stats;
using Tasklog.Api.Features.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ActivityLogInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options
        .UseSqlite(builder.Configuration.GetConnectionString("Default"))
        .AddInterceptors(sp.GetRequiredService<ActivityLogInterceptor>()));

// Enums as "High" instead of 2 in JSON.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// In development the Vite dev server proxies /api, so CORS is only needed
// when the frontend is served from another origin (e.g. a cloud deploy).
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

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

var api = app.MapGroup("/api");
api.MapTaskEndpoints();
api.MapCategoryEndpoints();
api.MapActivityEndpoints();
api.MapJournalEndpoints();
api.MapStatsEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

// Lets a future test project use WebApplicationFactory<Program>.
public partial class Program { }
