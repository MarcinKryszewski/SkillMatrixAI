using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkillMatrix.Api.Contracts;
using SkillMatrix.Api.Data;
using SkillMatrix.Api.Domain;

namespace SkillMatrix.Api.Endpoints;

internal static class LearningEndpoints
{
    public static RouteGroupBuilder MapLearningEndpoints(this RouteGroupBuilder api)
    {
        var instructions = api.MapGroup("/instructions").RequireAuthorization();
        instructions.MapGet("/", ListInstructions);
        instructions.MapPost("/", CreateInstruction)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator, Roles.Trainer));
        instructions.MapGet("/{id:int}", GetInstruction);
        instructions.MapGet("/{id:int}/edit", GetInstructionForEdit)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator, Roles.Trainer));
        instructions.MapPut("/{id:int}", UpdateInstruction)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator, Roles.Trainer));
        instructions.MapGet("/{id:int}/test", GetTest)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Employee));
        instructions.MapPost("/{id:int}/attempts", SubmitAttempt)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Employee));
        instructions.MapGet("/{id:int}/attempts", GetAttempts);
        instructions.MapPost("/{id:int}/retake-requests", RequestRetake)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Employee));

        api.MapGet("/retake-requests", ListRetakeRequests)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator, Roles.Trainer));
        api.MapPost("/retake-requests/{id:int}/review", ReviewRetake)
            .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator, Roles.Trainer));
        api.MapGet("/attempts", ListOwnAttempts).RequireAuthorization(policy => policy.RequireRole(Roles.Employee));
        api.MapGet("/skills", GetSkillMatrix).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> ListInstructions(
        HttpContext context,
        AppDbContext db,
        int? departmentId,
        int? machineId)
    {
        var query = db.Instructions.AsNoTracking().Include(x => x.Department).Include(x => x.Machine).AsQueryable();
        if (!context.User.IsInRole(Roles.Administrator)
            && (context.User.IsInRole(Roles.Employee) || context.User.IsInRole(Roles.Trainer)))
        {
            var current = await db.Users.AsNoTracking().SingleAsync(x => x.Id == context.User.UserId());
            if (current.DepartmentId is null)
            {
                return Results.Forbid();
            }

            query = query.Where(x => x.DepartmentId == current.DepartmentId);
        }
        else if (departmentId.HasValue)
        {
            query = query.Where(x => x.DepartmentId == departmentId.Value);
        }

        if (machineId.HasValue)
        {
            query = query.Where(x => x.MachineId == machineId.Value);
        }

        if (context.User.IsInRole(Roles.Employee)
            && !context.User.IsInRole(Roles.Administrator))
        {
            query = query.Where(x => x.IsPublished);
        }

        return Results.Ok(await query.OrderBy(x => x.Machine!.Name).ThenBy(x => x.SkillLevel)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.DepartmentId,
                DepartmentName = x.Department!.Name,
                x.MachineId,
                MachineName = x.Machine!.Name,
                x.SkillLevel,
                x.PassingScore,
                x.IsPublished,
                x.AuthorId,
                x.UpdatedAt,
                QuestionCount = x.Questions.Count
            }).ToListAsync());
    }

    private static async Task<IResult> CreateInstruction(
        InstructionInput input,
        HttpContext context,
        AppDbContext db)
    {
        var invalid = ValidateInstruction(input);
        if (invalid is not null)
        {
            return Results.BadRequest(new { error = invalid });
        }

        if (!await db.Departments.AnyAsync(x => x.Id == input.DepartmentId)
            || !await ApiAccess.CanAccessDepartmentAsync(context.User, input.DepartmentId, db)
            || !await db.Machines.AnyAsync(x => x.Id == input.MachineId))
        {
            return Results.BadRequest(new { error = "Choose an available department and machine." });
        }

        var instruction = new Instruction
        {
            Title = input.Title.Trim(),
            Content = input.Content,
            DepartmentId = input.DepartmentId,
            MachineId = input.MachineId,
            SkillLevel = input.SkillLevel,
            PassingScore = input.PassingScore,
            IsPublished = input.IsPublished,
            AuthorId = context.User.UserId(),
            Questions = BuildQuestions(input.Questions)
        };
        db.Instructions.Add(instruction);
        await db.SaveChangesAsync();
        return Results.Created($"/api/instructions/{instruction.Id}", new { instruction.Id });
    }

    private static async Task<IResult> UpdateInstruction(
        int id,
        InstructionInput input,
        HttpContext context,
        AppDbContext db)
    {
        var invalid = ValidateInstruction(input);
        if (invalid is not null)
        {
            return Results.BadRequest(new { error = invalid });
        }

        var instruction = await db.Instructions.Include(x => x.Questions)
            .ThenInclude(x => x.Choices).SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        if (!await ApiAccess.CanManageInstructionAsync(context.User, instruction, db))
        {
            return Results.Forbid();
        }

        if (!await db.Departments.AnyAsync(x => x.Id == input.DepartmentId)
            || !await ApiAccess.CanAccessDepartmentAsync(context.User, input.DepartmentId, db)
            || !await db.Machines.AnyAsync(x => x.Id == input.MachineId))
        {
            return Results.BadRequest(new { error = "Choose an available department and machine." });
        }

        instruction.Title = input.Title.Trim();
        instruction.Content = input.Content;
        instruction.DepartmentId = input.DepartmentId;
        instruction.MachineId = input.MachineId;
        instruction.SkillLevel = input.SkillLevel;
        instruction.PassingScore = input.PassingScore;
        instruction.IsPublished = input.IsPublished;
        instruction.UpdatedAt = DateTimeOffset.UtcNow;
        db.Questions.RemoveRange(instruction.Questions);
        instruction.Questions = BuildQuestions(input.Questions);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> GetInstruction(int id, HttpContext context, AppDbContext db)
    {
        var instruction = await db.Instructions.AsNoTracking()
            .Include(x => x.Department).Include(x => x.Machine)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        if (!await ApiAccess.CanAccessDepartmentAsync(context.User, instruction.DepartmentId, db)
            || (context.User.IsInRole(Roles.Employee)
                && !context.User.IsInRole(Roles.Administrator)
                && !instruction.IsPublished))
        {
            return Results.Forbid();
        }

        return Results.Ok(new
        {
            instruction.Id,
            instruction.Title,
            instruction.Content,
            instruction.DepartmentId,
            DepartmentName = instruction.Department!.Name,
            instruction.MachineId,
            MachineName = instruction.Machine!.Name,
            instruction.SkillLevel,
            instruction.PassingScore,
            instruction.IsPublished,
            instruction.AuthorId
        });
    }

    private static async Task<IResult> GetInstructionForEdit(int id, HttpContext context, AppDbContext db)
    {
        var instruction = await db.Instructions.AsNoTracking().Include(x => x.Questions)
            .ThenInclude(x => x.Choices).SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        if (!await ApiAccess.CanManageInstructionAsync(context.User, instruction, db))
        {
            return Results.Forbid();
        }

        return Results.Ok(new
        {
            instruction.Id,
            instruction.Title,
            instruction.Content,
            instruction.DepartmentId,
            instruction.MachineId,
            instruction.SkillLevel,
            instruction.PassingScore,
            instruction.IsPublished,
            Questions = instruction.Questions.OrderBy(x => x.Position).Select(question => new
            {
                question.Prompt,
                Choices = question.Choices.Select(choice => new { choice.Text, choice.IsCorrect })
            })
        });
    }

    private static async Task<IResult> GetTest(int id, HttpContext context, AppDbContext db)
    {
        var instruction = await db.Instructions.AsNoTracking().Include(x => x.Questions)
            .ThenInclude(x => x.Choices).SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        if (!instruction.IsPublished || !await ApiAccess.CanTakeInstructionAsync(
                context.User, instruction.DepartmentId, db))
        {
            return Results.Forbid();
        }

        return Results.Ok(new
        {
            instruction.Id,
            instruction.Title,
            instruction.Content,
            instruction.SkillLevel,
            instruction.PassingScore,
            Questions = instruction.Questions.OrderBy(x => x.Position).Select(question => new
            {
                question.Id,
                question.Prompt,
                Choices = question.Choices.Select(choice => new { choice.Id, choice.Text })
            })
        });
    }

    private static async Task<IResult> SubmitAttempt(
        int id,
        AttemptInput input,
        HttpContext context,
        AppDbContext db)
    {
        var instruction = await db.Instructions.Include(x => x.Questions)
            .ThenInclude(x => x.Choices).SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        var userId = context.User.UserId();
        if (!instruction.IsPublished || !await ApiAccess.CanTakeInstructionAsync(
                context.User, instruction.DepartmentId, db))
        {
            return Results.Forbid();
        }

        if (input.Answers is null
            || instruction.Questions.Count == 0
            || input.Answers.Count != instruction.Questions.Count
            || instruction.Questions.Any(question =>
                !input.Answers.TryGetValue(question.Id, out var choiceId)
                || question.Choices.All(choice => choice.Id != choiceId)))
        {
            return Results.BadRequest(new { error = "Answer every question with one of its listed choices." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var previousAttempts = await db.TestAttempts.Where(x =>
            x.UserId == userId && x.InstructionId == id).ToListAsync();
        RetakeRequest? approvedRequest = null;
        if (previousAttempts.Count > 0)
        {
            approvedRequest = await db.RetakeRequests.SingleOrDefaultAsync(x =>
                x.UserId == userId && x.InstructionId == id && x.Status == RetakeStatuses.Approved);
            if (approvedRequest is null)
            {
                return Results.Conflict(new { error = "An author-approved retake is required." });
            }

            approvedRequest.Status = RetakeStatuses.Consumed;
            approvedRequest.ReviewedAt = DateTimeOffset.UtcNow;
        }

        var correctCount = instruction.Questions.Count(question =>
            question.Choices.Single(choice => choice.IsCorrect).Id == input.Answers[question.Id]);
        var score = (int)Math.Round(100d * correctCount / instruction.Questions.Count,
            MidpointRounding.AwayFromZero);
        var attempt = new TestAttempt
        {
            InstructionId = id,
            UserId = userId,
            AttemptNumber = previousAttempts.Count + 1,
            Score = score,
            Passed = score >= instruction.PassingScore
        };
        db.TestAttempts.Add(attempt);

        try
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { error = "This test submission conflicts with another attempt. Reload and try again." });
        }

        return Results.Ok(new
        {
            attempt.Id,
            attempt.AttemptNumber,
            attempt.Score,
            attempt.Passed,
            instruction.PassingScore,
            attempt.CompletedAt
        });
    }

    private static async Task<IResult> GetAttempts(int id, HttpContext context, AppDbContext db)
    {
        var instruction = await db.Instructions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        var userId = context.User.UserId();
        if (context.User.IsInRole(Roles.Employee)
            && !context.User.IsInRole(Roles.Administrator)
            && !await ApiAccess.CanAccessDepartmentAsync(context.User, instruction.DepartmentId, db))
        {
            return Results.Forbid();
        }

        if (!context.User.IsInRole(Roles.Administrator)
            && !context.User.IsInRole(Roles.Employee)
            && instruction.AuthorId != userId)
        {
            return Results.Forbid();
        }

        var query = db.TestAttempts.AsNoTracking().Where(x => x.InstructionId == id);
        if (context.User.IsInRole(Roles.Employee)
            && !context.User.IsInRole(Roles.Administrator))
        {
            query = query.Where(x => x.UserId == userId);
        }

        return Results.Ok(await query.OrderByDescending(x => x.CompletedAt)
            .Select(x => new { x.Id, x.UserId, x.AttemptNumber, x.Score, x.Passed, x.CompletedAt })
            .ToListAsync());
    }

    private static async Task<IResult> RequestRetake(
        int id,
        HttpContext context,
        AppDbContext db)
    {
        var userId = context.User.UserId();
        var instruction = await db.Instructions.SingleOrDefaultAsync(x => x.Id == id);
        if (instruction is null)
        {
            return Results.NotFound();
        }

        if (!instruction.IsPublished || !await ApiAccess.CanTakeInstructionAsync(
                context.User, instruction.DepartmentId, db))
        {
            return Results.Forbid();
        }

        if (!await db.TestAttempts.AnyAsync(x => x.InstructionId == id && x.UserId == userId))
        {
            return Results.BadRequest(new { error = "Take the test before requesting a retake." });
        }

        var request = await db.RetakeRequests.SingleOrDefaultAsync(x =>
            x.InstructionId == id && x.UserId == userId);
        if (request is not null && request.Status is RetakeStatuses.Pending or RetakeStatuses.Approved)
        {
            return Results.Conflict(new { error = "A retake request is already pending or approved." });
        }

        if (request is null)
        {
            request = new RetakeRequest { InstructionId = id, UserId = userId };
            db.RetakeRequests.Add(request);
        }
        else
        {
            request.Status = RetakeStatuses.Pending;
            request.RequestedAt = DateTimeOffset.UtcNow;
            request.ReviewedAt = null;
            request.ReviewedByUserId = null;
        }

        await db.SaveChangesAsync();
        return Results.Accepted($"/api/retake-requests/{request.Id}", new
        {
            request.Id,
            request.Status,
            request.RequestedAt
        });
    }

    private static async Task<IResult> ListRetakeRequests(
        HttpContext context,
        AppDbContext db,
        string? status)
    {
        var query = db.RetakeRequests.AsNoTracking().Include(x => x.User)
            .Include(x => x.Instruction).ThenInclude(x => x!.Machine).AsQueryable();
        if (!context.User.IsInRole(Roles.Administrator))
        {
            var trainerId = context.User.UserId();
            query = query.Where(x => x.Instruction!.AuthorId == trainerId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        return Results.Ok(await query.OrderByDescending(x => x.RequestedAt)
            .Select(x => new
            {
                x.Id,
                x.Status,
                x.RequestedAt,
                UserEmail = x.User!.Email,
                InstructionTitle = x.Instruction!.Title,
                MachineName = x.Instruction.Machine!.Name,
                x.InstructionId
            }).ToListAsync());
    }

    private static async Task<IResult> ReviewRetake(
        int id,
        RetakeReviewInput input,
        HttpContext context,
        AppDbContext db)
    {
        var request = await db.RetakeRequests.Include(x => x.Instruction)
            .SingleOrDefaultAsync(x => x.Id == id);
        if (request is null)
        {
            return Results.NotFound();
        }

        if (!context.User.IsInRole(Roles.Administrator)
            && request.Instruction!.AuthorId != context.User.UserId())
        {
            return Results.Forbid();
        }

        if (request.Status != RetakeStatuses.Pending)
        {
            return Results.Conflict(new { error = "Only pending requests can be reviewed." });
        }

        request.Status = input.Approve ? RetakeStatuses.Approved : RetakeStatuses.Denied;
        request.ReviewedAt = DateTimeOffset.UtcNow;
        request.ReviewedByUserId = context.User.UserId();
        await db.SaveChangesAsync();
        return Results.Ok(new { request.Id, request.Status, request.ReviewedAt });
    }

    private static async Task<IResult> ListOwnAttempts(HttpContext context, AppDbContext db) =>
        Results.Ok(await db.TestAttempts.AsNoTracking()
            .Where(x => x.UserId == context.User.UserId())
            .Include(x => x.Instruction).ThenInclude(x => x!.Machine)
            .OrderByDescending(x => x.CompletedAt)
            .Select(x => new
            {
                x.Id,
                x.InstructionId,
                InstructionTitle = x.Instruction!.Title,
                MachineName = x.Instruction.Machine!.Name,
                x.Instruction.SkillLevel,
                x.AttemptNumber,
                x.Score,
                x.Passed,
                x.CompletedAt
            }).ToListAsync());

    private static async Task<IResult> GetSkillMatrix(
        HttpContext context,
        AppDbContext db,
        string? userId)
    {
        var requestedUserId = userId ?? context.User.UserId();
        if (context.User.IsInRole(Roles.Employee)
            && !context.User.IsInRole(Roles.Administrator)
            && requestedUserId != context.User.UserId())
        {
            return Results.Forbid();
        }

        var target = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestedUserId);
        if (target?.DepartmentId is null)
        {
            return Results.NotFound();
        }

        if (!await ApiAccess.CanAccessDepartmentAsync(context.User, target.DepartmentId.Value, db))
        {
            return Results.Forbid();
        }

        var machines = await db.Machines.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
        var passed = await db.TestAttempts.AsNoTracking()
            .Where(x => x.UserId == requestedUserId && x.Passed)
            .Select(x => new { x.Instruction!.MachineId, x.Instruction.SkillLevel })
            .ToListAsync();
        return Results.Ok(new
        {
            UserId = target.Id,
            target.Email,
            target.DepartmentId,
            Machines = machines.Select(machine =>
            {
                var levels = Enumerable.Range(1, 5).Select(level => new
                {
                    Level = level,
                    Passed = passed.Any(x => x.MachineId == machine.Id && x.SkillLevel == level)
                }).ToList();
                return new
                {
                    machine.Id,
                    machine.Name,
                    Levels = levels,
                    HighestPassedLevel = levels.Where(x => x.Passed).Select(x => (int?)x.Level).Max()
                };
            })
        });
    }

    private static string? ValidateInstruction(InstructionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Content))
        {
            return "Instruction title and content are required.";
        }

        if (input.SkillLevel is < 1 or > 5 || input.PassingScore is < 1 or > 100)
        {
            return "Skill level must be between 1 and 5, and passing score between 1 and 100.";
        }

        if (input.Questions is null || input.Questions.Count == 0)
        {
            return "Add at least one test question.";
        }

        foreach (var question in input.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Prompt)
                || question.Choices is null
                || question.Choices.Count < 2
                || question.Choices.Any(choice => string.IsNullOrWhiteSpace(choice.Text))
                || question.Choices.Count(choice => choice.IsCorrect) != 1)
            {
                return "Every question needs a prompt, at least two choices, and exactly one correct answer.";
            }
        }

        return null;
    }

    private static List<Question> BuildQuestions(List<QuestionInput> inputs) =>
        inputs.Select((question, index) => new Question
        {
            Prompt = question.Prompt.Trim(),
            Position = index,
            Choices = question.Choices.Select(choice => new AnswerChoice
            {
                Text = choice.Text.Trim(),
                IsCorrect = choice.IsCorrect
            }).ToList()
        }).ToList();
}
