using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tasklog.Api.Domain;

namespace Tasklog.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<SubTask> SubTasks => Set<SubTask>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no real datetime type and returns DateTimeKind.Unspecified.
        // Every DateTime in this app is UTC, so mark it as such on the way out;
        // otherwise JSON would be serialized without "Z" and the browser would
        // read it as local time.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

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

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
