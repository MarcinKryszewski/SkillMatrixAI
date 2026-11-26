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
            if (string.IsNullOrWhiteSpace(email)
                || string.IsNullOrWhiteSpace(request.Password)
                || request.Role is not (Roles.Administrator or Roles.Trainer or Roles.Employee))
            {
                return Results.BadRequest(new { error = "Provide a valid email, password, and role." });
            }

            if (request.Role != Roles.Administrator
                && (request.DepartmentId is null
                    || !await db.Departments.AnyAsync(x => x.Id == request.DepartmentId)))
            {
                return Results.BadRequest(new { error = "A valid department is required for trainers and employees." });
            }

            var user = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DepartmentId = request.Role == Roles.Administrator ? null : request.DepartmentId
            };
            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return Results.BadRequest(new { errors = created.Errors.Select(x => x.Description) });
            }

            var assigned = await userManager.AddToRoleAsync(user, request.Role);
            if (!assigned.Succeeded)
            {
                await userManager.DeleteAsync(user);
                return Results.Problem(
                    "The account was created but its role could not be assigned.",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Created($"/api/users/{user.Id}", new
            {
                user.Id,
                user.Email,
                user.DepartmentId,
                Role = request.Role
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
}
