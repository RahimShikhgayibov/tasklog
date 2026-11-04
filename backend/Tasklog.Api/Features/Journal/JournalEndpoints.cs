using Microsoft.EntityFrameworkCore;
using Tasklog.Api.Common;
using Tasklog.Api.Data;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Journal;

public sealed record JournalDto(
    DateOnly Date,
    string? Done,
    string? Learned,
    string? Blockers,
    string? TomorrowPlan,
    int? MinutesSpent,
    DateTime UpdatedAt);

public sealed record SaveJournalRequest(
    string? Done,
    string? Learned,
    string? Blockers,
    string? TomorrowPlan,
    int? MinutesSpent);

public static class JournalEndpoints
{
    private const int MaxTextLength = 8000;

    public static RouteGroupBuilder MapJournalEndpoints(this RouteGroupBuilder api)
    {
        var journal = api.MapGroup("/journal").WithTags("Journal");

        journal.MapGet("/", async (AppDbContext db, int? limit, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 10, 1, 100);
            var entries = await db.JournalEntries.AsNoTracking()
                .OrderByDescending(j => j.Date)
                .Take(take)
                .ToListAsync(ct);
            return Results.Ok(entries.Select(ToDto));
        });

        // 404 simply means "nothing written for that day yet".
        journal.MapGet("/{date}", async (DateOnly date, AppDbContext db, CancellationToken ct) =>
        {
            var entry = await db.JournalEntries.AsNoTracking().FirstOrDefaultAsync(j => j.Date == date, ct);
            return entry is null ? Results.NotFound() : Results.Ok(ToDto(entry));
        });

        // Upsert: one entry per day.
        journal.MapPut("/{date}", async (DateOnly date, SaveJournalRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var validation = new Validation()
                .Require(Fits(req.Done) && Fits(req.Learned) && Fits(req.Blockers) && Fits(req.TomorrowPlan),
                    "text", $"Hər sahə ən çox {MaxTextLength} simvol ola bilər.")
                .Require(req.MinutesSpent is null or (>= 0 and <= 24 * 60),
                    "minutesSpent", "Vaxt 0 ilə 1440 dəqiqə arasında olmalıdır.");
            if (!validation.IsValid) return validation.ToResult();

            var entry = await db.JournalEntries.FirstOrDefaultAsync(j => j.Date == date, ct);
            if (entry is null)
            {
                entry = new JournalEntry { Date = date };
                db.JournalEntries.Add(entry);
            }

            entry.Done = Clean(req.Done);
            entry.Learned = Clean(req.Learned);
            entry.Blockers = Clean(req.Blockers);
            entry.TomorrowPlan = Clean(req.TomorrowPlan);
            entry.MinutesSpent = req.MinutesSpent;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(entry));
        });

        return api;
    }

    private static bool Fits(string? s) => s is null || s.Length <= MaxTextLength;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static JournalDto ToDto(JournalEntry j) =>
        new(j.Date, j.Done, j.Learned, j.Blockers, j.TomorrowPlan, j.MinutesSpent, j.UpdatedAt);
}
