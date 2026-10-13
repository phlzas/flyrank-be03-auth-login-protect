using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using FlyRank.Auth.Api.Data;
using FlyRank.Auth.Api.Middleware;
using FlyRank.Auth.Api.Models;
using FlyRank.Auth.Api.Options;
using FlyRank.Auth.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

// TraversePath finds the repo-root .env from any working directory.
DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration -----------------------------------------------------------------

// Fail fast if JWT_SECRET is missing/weak.
var jwtOptions = JwtOptions.FromEnvironment();
builder.Services.AddSingleton(jwtOptions);

var databasePath = Environment.GetEnvironmentVariable("DATABASE") ?? "flyrank_auth.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

// ---- Identity (user store + password hashing) --------------------------------------

builder.Services
    .AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ---- Authentication (JWT bearer) + Authorization -----------------------------------

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role,
        };

        // Every auth failure answers with a JSON body instead of an empty 401,
        // so the API matches the {"error": "..."} contract for all 4xx responses.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var header = context.Request.Headers.Authorization.ToString();
                if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = header["Bearer ".Length..].Trim();
                    context.HttpContext.Items["BearerTokenPresent"] = true;
                }

                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                // Distinguish a missing/malformed token ("required") from a rejected one.
                var message = context.HttpContext.Items.ContainsKey("BearerTokenPresent")
                    ? "Invalid or expired token"
                    : "Access token required";
                await context.HttpContext.WriteErrorAsync(StatusCodes.Status401Unauthorized, message);
            },
            OnForbidden = async context =>
            {
                await context.HttpContext.WriteErrorAsync(
                    StatusCodes.Status403Forbidden,
                    "Forbidden: you do not have access to this resource.");
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Authenticated caller must carry a "role" claim of "admin".
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("role", "admin"));
});

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Automatic model-validation failures return {"error": "..."} like every other 4xx.
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState
            .SelectMany(kvp => kvp.Value!.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Invalid request.";

        return new BadRequestObjectResult(new { error = message });
    };
});

// ---- Rate limiting (brute-force protection on /auth/login) -------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.WriteErrorAsync(
            StatusCodes.Status429TooManyRequests,
            "Too many login attempts. Please try again later.",
            cancellationToken);
    };
});

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddControllers();

// Override the [ApiController] default so automatic model-validation 400s also use
// our {"error": "..."} shape. PostConfigure guarantees this runs after MVC's own setup.
builder.Services.PostConfigure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState
            .SelectMany(kvp => kvp.Value!.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Invalid request.";

        return new BadRequestObjectResult(new { error = message });
    };
});

// ---- Swagger / OpenAPI (served at /docs) -------------------------------------------

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FlyRank BE-03 - Auth: Login & Protect",
        Version = "v1",
        Description = "Secure API with sign-up, login, logout and JWT-protected routes. " +
                      "Built on ASP.NET Core Identity + JWT bearer.",
    });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the raw access token from /auth/login (no 'Bearer ' prefix).",
        Reference = new OpenApiReference
        {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer",
        },
    };

    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { scheme, Array.Empty<string>() },
    });
});

// ---- Port --------------------------------------------------------------------------

var port = Environment.GetEnvironmentVariable("PORT") ?? "3000";
builder.WebHost.UseUrls($"http://localhost:{port}");

var app = builder.Build();

// Apply EF Core migrations on boot (creates the SQLite file on first run).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// ---- Pipeline ----------------------------------------------------------------------

app.UseSwagger(c => c.RouteTemplate = "docs/{documentName}/swagger.json");
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "docs";
    c.SwaggerEndpoint("/docs/v1/swagger.json", "FlyRank Auth API v1");
    c.DocumentTitle = "FlyRank BE-03 Auth API";
});

app.UseAuthentication();
// Reusable token-checking middleware: rejects revoked (logged-out) tokens.
app.UseMiddleware<RevokedTokenMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Friendly root: point humans at the docs.
app.MapGet("/", () => Results.Redirect("/docs"));

app.Run();

// Shared helper so every middleware/event writes the same {"error": "..."} shape.
internal static class HttpResponseErrorExtensions
{
    public static Task WriteErrorAsync(
        this HttpContext context,
        int statusCode,
        string message,
        CancellationToken cancellationToken = default)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new { error = message }, cancellationToken);
    }
}
