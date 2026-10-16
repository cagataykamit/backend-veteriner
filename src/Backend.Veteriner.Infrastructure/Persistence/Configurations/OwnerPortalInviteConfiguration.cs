using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Portal daveti; ham token yalnız e-postada, burada SHA-256 özeti.</summary>
public sealed class OwnerPortalInviteConfiguration : IEntityTypeConfiguration<OwnerPortalInvite>
{
    public void Configure(EntityTypeBuilder<OwnerPortalInvite> b)
    {
        b.ToTable("OwnerPortalInvites");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.Email).IsRequired().HasMaxLength(OwnerAccount.MaxEmailLength);
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(OwnerPortalInvite.TokenHashLength);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.CreatedByUserId).IsRequired();

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ClientId, x.Status });

        b.HasOne<Client>()
            .WithMany()
            .HasForeignKey(x => x.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
