namespace SkillMatrix.Api.Contracts;

public sealed record LoginRequest(string Email, string Password);
public sealed record DepartmentRequest(string Name);
public sealed record MachineRequest(string Name, string? Description);
public sealed record UserRequest(string Email, string Password, string Role, int? DepartmentId);
public sealed record ChoiceInput(string Text, bool IsCorrect);
public sealed record QuestionInput(string Prompt, List<ChoiceInput> Choices);
public sealed record InstructionInput(
    string Title,
    string Content,
    int DepartmentId,
    int MachineId,
    int SkillLevel,
    int PassingScore,
    bool IsPublished,
    List<QuestionInput> Questions);
public sealed record AttemptInput(Dictionary<int, int> Answers);
public sealed record RetakeReviewInput(bool Approve);
