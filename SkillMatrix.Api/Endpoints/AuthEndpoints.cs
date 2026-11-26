using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using SkillMatrix.Api.Domain;
using SkillMatrix.Api.Contracts;

namespace SkillMatrix.Api.Endpoints;

internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth");
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });
        auth.MapPost("/login", async (
            LoginRequest request,
            SignInManager<AppUser> signInManager,
            UserManager<AppUser> userManager) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Email and password are required." });
            }

            var user = await userManager.FindByEmailAsync(request.Email.Trim());
            if (user is null)
            {
                return Results.Unauthorized();
            }

            var result = await signInManager.PasswordSignInAsync(
                user, request.Password, isPersistent: true, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await UserSummary(user, userManager));
        });
        auth.MapPost("/logout", async (SignInManager<AppUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapGet("/me", async (HttpContext context, UserManager<AppUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(context.User);
            return user is null ? Results.Unauthorized() : Results.Ok(await UserSummary(user, userManager));
        }).RequireAuthorization();
        return api;
    }

    private static async Task<object> UserSummary(AppUser user, UserManager<AppUser> userManager) =>
        new
        {
            user.Id,
            user.Email,
            user.DepartmentId,
            Roles = await userManager.GetRolesAsync(user)
        };
}
