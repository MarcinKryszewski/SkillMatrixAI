using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkillMatrix.Api.Data;
using SkillMatrix.Api.Domain;
using SkillMatrix.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(dataDirectory, "skillmatrix.db")}";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddAuthorization();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "skillmatrix.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.SlidingExpiration = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "skillmatrix.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173"];
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();

await InitializeDatabaseAsync(app);

app.UseCors();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var isMutation = HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method);
    if (context.Request.Path.StartsWithSegments("/api")
        && isMutation
        && !context.Request.Path.StartsWithSegments("/api/auth/csrf"))
    {
        try
        {
            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing CSRF token." });
            return;
        }
    }

    await next();
});
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapManagementEndpoints();
api.MapLearningEndpoints();

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();

    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { Roles.Administrator, Roles.Trainer, Roles.Employee })
    {
        if (!await roles.RoleExistsAsync(role))
        {
            var result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Could not create role '{role}': "
                    + string.Join("; ", result.Errors.Select(error => error.Description)));
            }
        }
    }

    var email = app.Configuration["SEED_ADMIN_EMAIL"];
    var password = app.Configuration["SEED_ADMIN_PASSWORD"];
    if (string.IsNullOrWhiteSpace(email) != string.IsNullOrWhiteSpace(password))
    {
        throw new InvalidOperationException("Set both SEED_ADMIN_EMAIL and SEED_ADMIN_PASSWORD to seed the first administrator.");
    }

    if (!string.IsNullOrWhiteSpace(email))
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is null)
        {
            var admin = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(admin, password!);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException("Could not create the seed administrator: "
                    + string.Join("; ", created.Errors.Select(error => error.Description)));
            }

            var assigned = await users.AddToRoleAsync(admin, Roles.Administrator);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException("Could not assign the administrator role: "
                    + string.Join("; ", assigned.Errors.Select(error => error.Description)));
            }
        }
    }
}
