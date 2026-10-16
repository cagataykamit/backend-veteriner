using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Portal oturumları; personel RefreshTokens tablosundan ayrı.</summary>
public sealed class OwnerPortalRefreshTokenConfiguration : IEntityTypeConfiguration<OwnerPortalRefreshToken>
{
    public void Configure(EntityTypeBuilder<OwnerPortalRefreshToken> b)
    {
        b.ToTable("OwnerPortalRefreshTokens");
        b.HasKey(x => x.Id);

        b.Property(x => x.OwnerAccountId).IsRequired();
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(OwnerPortalRefreshToken.TokenHashLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.RevokeReason).HasMaxLength(OwnerPortalRefreshToken.MaxRevokeReasonLength);
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(OwnerPortalRefreshToken.TokenHashLength);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.OwnerAccountId);

        b.HasOne<OwnerAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
