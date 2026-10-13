using System.IdentityModel.Tokens.Jwt;
using FlyRank.Auth.Api.Data;
using FlyRank.Auth.Api.Dtos;
using FlyRank.Auth.Api.Models;
using FlyRank.Auth.Api.Options;
using FlyRank.Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FlyRank.Auth.Api.Controllers;

/// <summary>
/// Authentication endpoints: sign-up, login, logout and token refresh.
/// All responses use explicit HTTP status codes (201/200/204/400/401).
/// </summary>
[ApiController]
[Route("auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IJwtTokenService _tokens;
    private readonly ApplicationDbContext _db;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<AppUser> userManager,
        IJwtTokenService tokens,
        ApplicationDbContext db,
        JwtOptions jwtOptions,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _tokens = tokens;
        _db = db;
        _jwtOptions = jwtOptions;
        _logger = logger;
    }

    /// <summary>Registers a new user with email + password.</summary>
    /// <response code="201">User created.</response>
    /// <response code="400">Missing/invalid input, or email already registered.</response>
    [HttpPost("signup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var user = new AppUser
        {
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName,
        };

        // Identity hashes the password with PBKDF2 and enforces the configured policy.
        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(error.Code, error.Description);
            }

            return ValidationProblem(ModelState);
        }

        _logger.LogInformation("New user registered: {UserId}", user.Id);

        var dto = new UserDto(user.Id, user.Email!, user.FullName);
        return Created($"/auth/users/{user.Id}", dto);
    }

    /// <summary>Authenticates a user and returns an access token + refresh token.</summary>
    /// <response code="200">Authenticated.</response>
    /// <response code="400">Missing or invalid input.</response>
    /// <response code="401">Invalid email or password.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var user = await _userManager.FindByEmailAsync(request.Email);

        // Same generic response whether the user is unknown or the password is wrong,
        // so we never leak which emails are registered.
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Invalid email or password.",
            });
        }

        var accessToken = _tokens.CreateAccessToken(user, out _, out var expiresAt);
        var refreshToken = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            IsRevoked = false,
        });
        await _db.SaveChangesAsync();

        var response = new AuthResponse(
            accessToken,
            refreshToken,
            "Bearer",
            (int)Math.Max(0, (expiresAt - DateTimeOffset.UtcNow).TotalSeconds),
            new UserDto(user.Id, user.Email!, user.FullName));

        return Ok(response);
    }

    /// <summary>Logs out the caller by revoking the current access token and refresh token.</summary>
    /// <response code="204">Logged out.</response>
    /// <response code="401">Missing or invalid token.</response>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest? request)
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
                  ?? User.FindFirst("jti")?.Value;

        if (!string.IsNullOrEmpty(jti))
        {
            // Deny-list the access token until it would naturally expire.
            var alreadyRevoked = await _db.RevokedAccessTokens.AnyAsync(t => t.Jti == jti);
            if (!alreadyRevoked)
            {
                _db.RevokedAccessTokens.Add(new RevokedAccessToken
                {
                    Jti = jti,
                    RevokedAt = DateTimeOffset.UtcNow,
                    // Upper bound: issued-at + access lifetime. Safe because the token
                    // cannot outlive this window anyway.
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
                });
            }
        }

        // Revoke the presented refresh token (if any) so it can't mint new sessions.
        if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
        {
            var stored = await _db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);
            if (stored is not null)
            {
                stored.IsRevoked = true;
            }
        }

        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Exchanges a valid refresh token for a new access token (rotation).</summary>
    /// <response code="200">Refreshed.</response>
    /// <response code="400">Missing input.</response>
    /// <response code="401">Refresh token invalid, expired or revoked.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken);

        if (stored is null || stored.IsRevoked || stored.ExpiresAt <= DateTimeOffset.UtcNow || stored.User is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "Refresh token is invalid, expired or revoked.",
            });
        }

        // Rotate: retire the old refresh token, issue a brand new pair.
        stored.IsRevoked = true;

        var user = stored.User;
        var accessToken = _tokens.CreateAccessToken(user, out _, out var expiresAt);
        var newRefresh = _tokens.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefresh,
            UserId = user.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
            IsRevoked = false,
        });
        await _db.SaveChangesAsync();

        var response = new AuthResponse(
            accessToken,
            newRefresh,
            "Bearer",
            (int)Math.Max(0, (expiresAt - DateTimeOffset.UtcNow).TotalSeconds),
            new UserDto(user.Id, user.Email!, user.FullName));

        return Ok(response);
    }
}
