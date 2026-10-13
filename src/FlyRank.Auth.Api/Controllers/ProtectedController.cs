using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FlyRank.Auth.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlyRank.Auth.Api.Controllers;

/// <summary>
/// Protected endpoints. Every route here requires a valid Bearer access token;
/// missing/invalid/expired/revoked tokens yield 401 via the JWT bearer middleware.
/// </summary>
[ApiController]
[Route("protected")]
[Produces("application/json")]
[Authorize]
public class ProtectedController : ControllerBase
{
    /// <summary>Returns profile information for the authenticated user.</summary>
    /// <response code="200">Authenticated; returns the caller's profile.</response>
    /// <response code="401">Missing, invalid, expired or revoked token.</response>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<ProfileResponse> Profile()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                     ?? string.Empty;
        var email = User.FindFirst("email")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value
                    ?? string.Empty;
        var fullName = User.FindFirst("full_name")?.Value;
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? string.Empty;

        var issuedAt = ReadUnixTime("iat");
        var expiresAt = ReadUnixTime("exp");

        return Ok(new ProfileResponse(userId, email, fullName, jti, issuedAt, expiresAt));
    }

    /// <summary>Example second protected route used to prove the middleware is reusable.</summary>
    /// <response code="200">Authenticated; returns a personalised dashboard message.</response>
    /// <response code="401">Missing, invalid, expired or revoked token.</response>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<DashboardResponse> Dashboard()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                     ?? string.Empty;

        return Ok(new DashboardResponse(
            Message: "This is a protected dashboard. Only authenticated users can see it.",
            UserId: userId,
            ServerTime: DateTimeOffset.UtcNow));
    }

    private DateTimeOffset ReadUnixTime(string claimType)
    {
        var raw = User.FindFirst(claimType)?.Value;
        return long.TryParse(raw, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow;
    }
}
