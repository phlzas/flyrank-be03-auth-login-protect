using FlyRank.Auth.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FlyRank.Auth.Api.Data;

/// <summary>
/// EF Core context. Inherits the Identity schema (users, roles, claims...) and adds
/// our auth-support tables.
/// </summary>
public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<RevokedAccessToken> RevokedAccessTokens => Set<RevokedAccessToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Token).IsUnique();
            entity.Property(x => x.Token).IsRequired();
            entity.HasOne(x => x.User)
                  .WithMany()
                  .HasForeignKey(x => x.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RevokedAccessToken>(entity =>
        {
            entity.HasKey(x => x.Jti);
            entity.Property(x => x.Jti).IsRequired();
        });
    }
}
