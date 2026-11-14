using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Data;

/// <remarks>
/// PostgreSQL. Every DateTime is UTC and stored as "timestamp with time zone";
/// Npgsql refuses to write a DateTime whose Kind isn't Utc, which catches any
/// accidental local time early.
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<SubTask> SubTasks => Set<SubTask>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    /// <summary>
    /// ASP.NET Core's cookie-encryption keys. Kept in the database so a login
    /// survives restarts: the free cloud instance restarts every time it wakes up.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Ids are generated in C# (Guid v7). Telling EF they are never generated
        // by the database matters: a new SubTask added to a tracked task's list
        // already has an Id, and without this EF would treat it as an existing
        // row and send an UPDATE instead of an INSERT.
        modelBuilder.Entity<TaskItem>().Property(t => t.Id).ValueGeneratedNever();
        modelBuilder.Entity<SubTask>().Property(s => s.Id).ValueGeneratedNever();
        modelBuilder.Entity<Category>().Property(c => c.Id).ValueGeneratedNever();
        modelBuilder.Entity<JournalEntry>().Property(j => j.Id).ValueGeneratedNever();

        modelBuilder.Entity<TaskItem>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(300);
            e.Property(t => t.Description).HasMaxLength(4000);

            e.HasOne(t => t.Category)
                .WithMany()
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(t => t.SubTasks)
                .WithOne()
                .HasForeignKey(s => s.TaskItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(t => new { t.IsCompleted, t.DueAt });
            e.HasIndex(t => t.SeriesId);
            e.HasIndex(t => t.CreatedAt);
        });

        modelBuilder.Entity<SubTask>(e =>
        {
            e.Property(s => s.Title).HasMaxLength(300);
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.Property(c => c.Name).HasMaxLength(50);
            e.Property(c => c.Color).HasMaxLength(9);
            e.HasIndex(c => c.Name).IsUnique();
        });

        modelBuilder.Entity<ActivityEntry>(e =>
        {
            e.Property(a => a.Title).HasMaxLength(300);
            e.Property(a => a.ParentTitle).HasMaxLength(300);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.TaskId);
        });

        modelBuilder.Entity<JournalEntry>(e =>
        {
            e.HasIndex(j => j.Date).IsUnique();
        });
    }
}

/// <summary>
/// Used only by "dotnet ef migrations …": builds the model without starting
/// the app, so no real database or password is needed to create a migration.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=tasklog_design")
            .Options);
}
