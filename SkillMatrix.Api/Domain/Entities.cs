using Microsoft.AspNetCore.Identity;

namespace SkillMatrix.Api.Domain;

public static class Roles
{
    public const string Administrator = "Administrator";
    public const string Trainer = "Trainer";
    public const string Employee = "Employee";
}

public sealed class AppUser : IdentityUser
{
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
}

public sealed class Department
{
    public int Id { get; set; }
    public required string Name { get; set; }
}

public sealed class Machine
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}

public sealed class Instruction
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public int DepartmentId { get; set; }
    public Department? Department { get; set; }
    public int MachineId { get; set; }
    public Machine? Machine { get; set; }
    public int SkillLevel { get; set; }
    public int PassingScore { get; set; }
    public bool IsPublished { get; set; }
    public required string AuthorId { get; set; }
    public AppUser? Author { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Question> Questions { get; set; } = [];
    public List<TestAttempt> Attempts { get; set; } = [];
}

public sealed class Question
{
    public int Id { get; set; }
    public int InstructionId { get; set; }
    public Instruction? Instruction { get; set; }
    public required string Prompt { get; set; }
    public int Position { get; set; }
    public List<AnswerChoice> Choices { get; set; } = [];
}

public sealed class AnswerChoice
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    public required string Text { get; set; }
    public bool IsCorrect { get; set; }
}

public sealed class TestAttempt
{
    public int Id { get; set; }
    public int InstructionId { get; set; }
    public Instruction? Instruction { get; set; }
    public required string UserId { get; set; }
    public AppUser? User { get; set; }
    public int AttemptNumber { get; set; }
    public int Score { get; set; }
    public bool Passed { get; set; }
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RetakeRequest
{
    public int Id { get; set; }
    public int InstructionId { get; set; }
    public Instruction? Instruction { get; set; }
    public required string UserId { get; set; }
    public AppUser? User { get; set; }
    public string Status { get; set; } = RetakeStatuses.Pending;
    public DateTimeOffset RequestedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public AppUser? ReviewedBy { get; set; }
}

public static class RetakeStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Denied = "Denied";
    public const string Consumed = "Consumed";
}
