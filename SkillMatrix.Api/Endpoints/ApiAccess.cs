using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SkillMatrix.Api.Data;
using SkillMatrix.Api.Domain;

namespace SkillMatrix.Api.Endpoints;

internal static class ApiAccess
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("The authenticated user has no identifier.");

    public static async Task<bool> CanManageInstructionAsync(
        ClaimsPrincipal user,
        Instruction instruction,
        AppDbContext db)
    {
        if (user.IsInRole(Roles.Administrator))
        {
            return true;
        }

        if (!user.IsInRole(Roles.Trainer))
        {
            return false;
        }

        var trainer = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.UserId());
        return trainer.DepartmentId == instruction.DepartmentId && instruction.AuthorId == trainer.Id;
    }

    public static async Task<bool> CanAccessDepartmentAsync(
        ClaimsPrincipal user,
        int departmentId,
        AppDbContext db)
    {
        if (user.IsInRole(Roles.Administrator))
        {
            return true;
        }

        if (!user.IsInRole(Roles.Trainer) && !user.IsInRole(Roles.Employee))
        {
            return false;
        }

        var current = await db.Users.AsNoTracking().SingleAsync(x => x.Id == user.UserId());
        return current.DepartmentId == departmentId;
    }
}
