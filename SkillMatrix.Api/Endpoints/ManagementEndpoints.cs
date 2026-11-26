using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkillMatrix.Api.Contracts;
using SkillMatrix.Api.Data;
using SkillMatrix.Api.Domain;

namespace SkillMatrix.Api.Endpoints;

internal static class ManagementEndpoints
{
    public static RouteGroupBuilder MapManagementEndpoints(this RouteGroupBuilder api)
    {
        var departments = api.MapGroup("/departments").RequireAuthorization();
        departments.MapGet("/", async (AppDbContext db) =>
            Results.Ok(await db.Departments.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name }).ToListAsync()));
        departments.MapPost("/", async (DepartmentRequest request, AppDbContext db) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { error = "Department name is required." });
            }

            var department = new Department { Name = name };
            db.Departments.Add(department);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "A department with this name already exists." });
            }

            return Results.Created($"/api/departments/{department.Id}", new { department.Id, department.Name });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));

        var machines = api.MapGroup("/machines").RequireAuthorization();
        machines.MapGet("/", async (AppDbContext db) =>
            Results.Ok(await db.Machines.AsNoTracking().OrderBy(x => x.Name)
                .Select(x => new { x.Id, x.Name, x.Description }).ToListAsync()));
        machines.MapPost("/", async (MachineRequest request, AppDbContext db) =>
        {
            var name = request.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { error = "Machine name is required." });
            }

            var machine = new Machine { Name = name, Description = request.Description?.Trim() };
            db.Machines.Add(machine);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "A machine with this name already exists." });
            }

            return Results.Created($"/api/machines/{machine.Id}",
                new { machine.Id, machine.Name, machine.Description });
        }).RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));

        var users = api.MapGroup("/users")
            .RequireAuthorization(policy => policy.RequireRole(Roles.Administrator));
        users.MapGet("/", async (AppDbContext db, UserManager<AppUser> userManager) =>
        {
            var allUsers = await db.Users.AsNoTracking().Include(x => x.Department)
                .OrderBy(x => x.Email).ToListAsync();
            var result = new List<object>();
            foreach (var user in allUsers)
            {
                result.Add(new
                {
                    user.Id,
                    user.Email,
                    user.DepartmentId,
                    DepartmentName = user.Department?.Name,
                    Roles = await userManager.GetRolesAsync(user)
                });
            }

            return Results.Ok(result);
        });
        users.MapPost("/", async (
            UserRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager) =>
        {
            var email = request.Email?.Trim();
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Provide a valid email and password." });
            }

            var roleNames = NormalizeRoles(request.Roles, out var roleError);
            if (roleNames is null)
            {
                return Results.BadRequest(new { error = roleError });
            }

            var departmentError = await ValidateDepartmentAsync(roleNames, request.DepartmentId, db);
            if (departmentError is not null)
            {
                return Results.BadRequest(new { error = departmentError });
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DepartmentId = RequiresDepartment(roleNames) ? request.DepartmentId : null
            };
            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return Results.BadRequest(new { errors = created.Errors.Select(x => x.Description) });
            }

            var assigned = await userManager.AddToRolesAsync(user, roleNames);
            if (!assigned.Succeeded)
            {
                return Results.BadRequest(new { errors = assigned.Errors.Select(x => x.Description) });
            }

            await transaction.CommitAsync();
            return Results.Created($"/api/users/{user.Id}", new
            {
                user.Id,
                user.Email,
                user.DepartmentId,
                Roles = roleNames
            });
        });
        users.MapPut("/{id}", async (
            string id,
            UserRolesUpdateRequest request,
            AppDbContext db,
            UserManager<AppUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null)
            {
                return Results.NotFound();
            }

            var roleNames = NormalizeRoles(request.Roles, out var roleError);
            if (roleNames is null)
            {
                return Results.BadRequest(new { error = roleError });
            }

            var departmentError = await ValidateDepartmentAsync(roleNames, request.DepartmentId, db);
            if (departmentError is not null)
            {
                return Results.BadRequest(new { error = departmentError });
            }

            var currentRoles = await userManager.GetRolesAsync(user);
            var departmentId = RequiresDepartment(roleNames) ? request.DepartmentId : null;
            if (user.DepartmentId == departmentId
                && currentRoles.Order(StringComparer.Ordinal)
                    .SequenceEqual(roleNames.Order(StringComparer.Ordinal)))
            {
                return Results.Ok(new
                {
                    user.Id,
                    user.Email,
                    user.DepartmentId,
                    Roles = currentRoles
                });
            }

            await using var transaction = await db.Database.BeginTransactionAsync();
            if (currentRoles.Contains(Roles.Administrator)
                && !roleNames.Contains(Roles.Administrator))
            {
                var administrators = await userManager.GetUsersInRoleAsync(Roles.Administrator);
                if (administrators.Count <= 1)
                {
                    return Results.Conflict(new { error = "At least one administrator account must remain." });
                }
            }

            var rolesToRemove = currentRoles.Except(roleNames).ToArray();
            if (rolesToRemove.Length > 0)
            {
                var removed = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
                if (!removed.Succeeded)
                {
                    return Results.BadRequest(new { errors = removed.Errors.Select(x => x.Description) });
                }
            }

            var rolesToAdd = roleNames.Except(currentRoles).ToArray();
            if (rolesToAdd.Length > 0)
            {
                var added = await userManager.AddToRolesAsync(user, rolesToAdd);
                if (!added.Succeeded)
                {
                    return Results.BadRequest(new { errors = added.Errors.Select(x => x.Description) });
                }
            }

            user.DepartmentId = departmentId;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return Results.BadRequest(new { errors = updated.Errors.Select(x => x.Description) });
            }

            var stampUpdated = await userManager.UpdateSecurityStampAsync(user);
            if (!stampUpdated.Succeeded)
            {
                return Results.BadRequest(new { errors = stampUpdated.Errors.Select(x => x.Description) });
            }

            await transaction.CommitAsync();
            return Results.Ok(new
            {
                user.Id,
                user.Email,
                user.DepartmentId,
                Roles = roleNames
            });
        });
        api.MapGet("/department/users", async (HttpContext context, AppDbContext db) =>
        {
            if (!context.User.IsInRole(Roles.Administrator) && !context.User.IsInRole(Roles.Trainer))
            {
                return Results.Forbid();
            }

            var query = db.Users.AsNoTracking().Include(x => x.Department)
                .Where(x => x.DepartmentId != null).AsQueryable();
            if (context.User.IsInRole(Roles.Trainer))
            {
                var trainer = await db.Users.AsNoTracking().SingleAsync(x => x.Id == context.User.UserId());
                if (trainer.DepartmentId is null)
                {
                    return Results.Forbid();
                }

                query = query.Where(x => x.DepartmentId == trainer.DepartmentId && x.Id != trainer.Id);
            }

            var people = await query.OrderBy(x => x.Email)
                .Select(x => new { x.Id, x.Email, x.DepartmentId, DepartmentName = x.Department!.Name })
                .ToListAsync();
            return Results.Ok(people);
        }).RequireAuthorization();
        return api;
    }

    private static readonly string[] AvailableRoles =
        [Roles.Administrator, Roles.Trainer, Roles.Employee];

    private static List<string>? NormalizeRoles(List<string>? requestedRoles, out string? error)
    {
        error = null;
        if (requestedRoles is null || requestedRoles.Count == 0)
        {
            error = "Select at least one role.";
            return null;
        }

        var roles = new List<string>();
        foreach (var requestedRole in requestedRoles)
        {
            if (string.IsNullOrWhiteSpace(requestedRole))
            {
                error = "Role names cannot be empty.";
                return null;
            }

            var role = AvailableRoles.FirstOrDefault(available =>
                string.Equals(available, requestedRole.Trim(), StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                error = $"Unknown role: {requestedRole}.";
                return null;
            }

            if (roles.Contains(role, StringComparer.Ordinal))
            {
                error = $"Role '{role}' was selected more than once.";
                return null;
            }

            roles.Add(role);
        }

        return roles;
    }

    private static bool RequiresDepartment(IEnumerable<string> roles) =>
        roles.Contains(Roles.Trainer) || roles.Contains(Roles.Employee);

    private static async Task<string?> ValidateDepartmentAsync(
        IReadOnlyCollection<string> roles,
        int? departmentId,
        AppDbContext db)
    {
        if (RequiresDepartment(roles))
        {
            return departmentId is null || !await db.Departments.AnyAsync(x => x.Id == departmentId)
                ? "Choose a valid department for trainer and employee roles."
                : null;
        }

        return departmentId is null
            ? null
            : "Administrator-only accounts do not have a department.";
    }
}
