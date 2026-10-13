using Microsoft.AspNetCore.Identity;

namespace FlyRank.Auth.Api.Models;

/// <summary>
/// Application user. Extends the framework's <see cref="IdentityUser"/>, which gives us
/// password hashing (PBKDF2), normalization and security-stamp handling for free.
/// </summary>
public class AppUser : IdentityUser
{
    /// <summary>Optional display name supplied at sign-up.</summary>
    public string? FullName { get; set; }
}
