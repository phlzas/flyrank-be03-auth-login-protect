using System.IdentityModel.Tokens.Jwt;
using FlyRank.Auth.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlyRank.Auth.Api.Middleware;

/// <summary>
/// Reusable middleware that rejects access tokens whose <c>jti</c> has been revoked
/// (i.e. the user logged out). Runs after authentication and before authorization /
/// route handlers, so every protected route benefits without extra code.
/// </summary>
public sealed class RevokedTokenMiddleware
{
    private readonly RequestDelegate _next;

    public RevokedTokenMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                      ?? context.User.FindFirst("jti")?.Value;

            if (!string.IsNullOrEmpty(jti) &&
                await db.RevokedAccessTokens.AnyAsync(t => t.Jti == jti))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Unauthorized",
                    Status = StatusCodes.Status401Unauthorized,
                    Detail = "This token has been revoked. Please log in again.",
                });
                return;
            }
        }

        await _next(context);
    }
}
