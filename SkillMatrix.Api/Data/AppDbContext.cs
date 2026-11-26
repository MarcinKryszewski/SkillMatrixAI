using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SkillMatrix.Api.Domain;

namespace SkillMatrix.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Instruction> Instructions => Set<Instruction>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<AnswerChoice> AnswerChoices => Set<AnswerChoice>();
    public DbSet<TestAttempt> TestAttempts => Set<TestAttempt>();
    public DbSet<RetakeRequest> RetakeRequests => Set<RetakeRequest>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var utcDateTimeOffset = new ValueConverter<DateTimeOffset, long>(
            value => value.UtcDateTime.Ticks,
            value => new DateTimeOffset(value, TimeSpan.Zero));
        var nullableUtcDateTimeOffset = new ValueConverter<DateTimeOffset?, long?>(
            value => value.HasValue ? value.Value.UtcDateTime.Ticks : null,
            value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null);

        builder.Entity<Department>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<Machine>().HasIndex(x => x.Name).IsUnique();
        builder.Entity<Instruction>().Property(x => x.UpdatedAt).HasConversion(utcDateTimeOffset);
        builder.Entity<TestAttempt>().Property(x => x.CompletedAt).HasConversion(utcDateTimeOffset);
        builder.Entity<RetakeRequest>().Property(x => x.RequestedAt).HasConversion(utcDateTimeOffset);
        builder.Entity<RetakeRequest>().Property(x => x.ReviewedAt).HasConversion(nullableUtcDateTimeOffset);
        builder.Entity<Instruction>().HasMany(x => x.Questions).WithOne(x => x.Instruction)
            .HasForeignKey(x => x.InstructionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Question>().HasMany(x => x.Choices).WithOne(x => x.Question)
            .HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<TestAttempt>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<RetakeRequest>().HasIndex(x => new { x.UserId, x.InstructionId }).IsUnique();
        builder.Entity<TestAttempt>().HasIndex(x => new { x.UserId, x.InstructionId, x.AttemptNumber }).IsUnique();
        builder.Entity<RetakeRequest>().HasOne(x => x.ReviewedBy).WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
