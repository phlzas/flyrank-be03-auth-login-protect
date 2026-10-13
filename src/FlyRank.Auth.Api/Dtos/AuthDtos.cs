using System.ComponentModel.DataAnnotations;

namespace FlyRank.Auth.Api.Dtos;

/// <summary>Payload for <c>POST /auth/signup</c>.</summary>
public sealed record SignUpRequest
{
    [Required(ErrorMessage = "email is required")]
    [EmailAddress(ErrorMessage = "email must be a valid email address")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "password is required")]
    [MinLength(8, ErrorMessage = "password must be at least 8 characters")]
    public string Password { get; init; } = string.Empty;

    public string? FullName { get; init; }
}

/// <summary>Payload for <c>POST /auth/login</c>.</summary>
public sealed record LoginRequest
{
    [Required(ErrorMessage = "email is required")]
    [EmailAddress(ErrorMessage = "email must be a valid email address")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "password is required")]
    public string Password { get; init; } = string.Empty;
}

/// <summary>Payload for <c>POST /auth/refresh</c>.</summary>
public sealed record RefreshRequest
{
    [Required(ErrorMessage = "refreshToken is required")]
    public string RefreshToken { get; init; } = string.Empty;
}

/// <summary>Public projection of a user.</summary>
public sealed record UserDto(string Id, string Email, string? FullName);

/// <summary>Returned by sign-up, login and refresh.</summary>
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserDto User);

/// <summary>Returned by <c>GET /protected/profile</c>.</summary>
public sealed record ProfileResponse(
    string Id,
    string Email,
    string? FullName,
    string TokenId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);

/// <summary>Returned by <c>GET /protected/dashboard</c>.</summary>
public sealed record DashboardResponse(
    string Message,
    string UserId,
    DateTimeOffset ServerTime);

/// <summary>Returned by <c>GET /public/info</c>.</summary>
public sealed record PublicInfoResponse(
    string Message,
    string Service,
    string Version,
    DateTimeOffset ServerTime,
    bool AuthenticationRequired);
