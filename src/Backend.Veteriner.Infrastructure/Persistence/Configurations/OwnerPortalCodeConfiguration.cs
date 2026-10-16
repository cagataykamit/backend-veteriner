using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Tek kullanımlık e-posta kodu.</summary>
public sealed class OwnerPortalCodeConfiguration : IEntityTypeConfiguration<OwnerPortalCode>
{
    public void Configure(EntityTypeBuilder<OwnerPortalCode> b)
    {
        b.ToTable("OwnerPortalCodes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.OwnerAccountId).IsRequired();
        b.Property(x => x.Purpose).IsRequired();
        b.Property(x => x.CodeHash).IsRequired().HasMaxLength(OwnerPortalCode.CodeHashLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.FailedAttempts).IsRequired();

        b.HasIndex(x => new { x.OwnerAccountId, x.Purpose, x.CreatedAtUtc });

        b.HasOne<OwnerAccount>()
            .WithMany()
            .HasForeignKey(x => x.OwnerAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
