using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tasklog.Api.Common;
using Tasklog.Api.Data;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Features.Categories;

public sealed record CategoryDto(Guid Id, string Name, string Color);

public sealed record SaveCategoryRequest(string Name, string Color);

public static partial class CategoryEndpoints
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public static RouteGroupBuilder MapCategoryEndpoints(this RouteGroupBuilder api)
    {
        var categories = api.MapGroup("/categories").WithTags("Categories");

        categories.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var list = await db.Categories.AsNoTracking()
                .OrderBy(c => c.CreatedAt)
                .ThenBy(c => c.Id) // Guid v7 → creation order
                .Select(c => new CategoryDto(c.Id, c.Name, c.Color))
                .ToListAsync(ct);
            return Results.Ok(list);
        });

        categories.MapPost("/", async (SaveCategoryRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var validation = await Validate(req, null, db, ct);
            if (!validation.IsValid) return validation.ToResult();

            var category = new Category { Name = req.Name.Trim(), Color = req.Color };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/categories/{category.Id}",
                new CategoryDto(category.Id, category.Name, category.Color));
        });

        categories.MapPut("/{id:guid}", async (Guid id, SaveCategoryRequest req, AppDbContext db, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null) return Results.NotFound();

            var validation = await Validate(req, id, db, ct);
            if (!validation.IsValid) return validation.ToResult();

            category.Name = req.Name.Trim();
            category.Color = req.Color;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new CategoryDto(category.Id, category.Name, category.Color));
        });

        // Tasks in a deleted category are kept; their CategoryId becomes null
        // (ON DELETE SET NULL in the database).
        categories.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var category = await db.Categories.FindAsync([id], ct);
            if (category is null) return Results.NotFound();

            db.Categories.Remove(category);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return api;
    }

    private static async Task<Validation> Validate(SaveCategoryRequest req, Guid? existingId, AppDbContext db, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? "";
        var validation = new Validation()
            .Require(name.Length is > 0 and <= 50, "name", "Ad 1–50 simvol olmalıdır.")
            .Require(req.Color is not null && HexColor().IsMatch(req.Color), "color", "Rəng #RRGGBB formatında olmalıdır.");

        if (name.Length > 0)
        {
            var taken = await db.Categories.AnyAsync(c => c.Name == name && c.Id != existingId, ct);
            validation.Require(!taken, "name", "Bu adda kateqoriya artıq var.");
        }

        return validation;
    }

    /// <summary>First run: create the four categories from the design.</summary>
    public static async Task SeedDefaultsAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(ct)) return;

        db.Categories.AddRange(
            new Category { Name = "İş", Color = "#3451D1" },
            new Category { Name = "Təhsil", Color = "#C2410C" },
            new Category { Name = "Şəxsi", Color = "#0F766E" },
            new Category { Name = "Layihələr", Color = "#7C3AED" });

        await db.SaveChangesAsync(ct);
    }
}
