using System.Security.Claims;
using System.Text;
using FlyRank.Auth.Api.Data;
using FlyRank.Auth.Api.Middleware;
using FlyRank.Auth.Api.Models;
using FlyRank.Auth.Api.Options;
using FlyRank.Auth.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
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
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddControllers();

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

app.MapControllers();

// Friendly root: point humans at the docs.
app.MapGet("/", () => Results.Redirect("/docs"));

app.Run();
