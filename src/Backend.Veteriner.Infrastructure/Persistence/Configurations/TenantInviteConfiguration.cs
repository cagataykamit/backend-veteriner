using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class TenantInviteConfiguration : IEntityTypeConfiguration<TenantInvite>
{
    public void Configure(EntityTypeBuilder<TenantInvite> b)
    {
        b.ToTable("TenantInvites");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Email).IsRequired().HasMaxLength(200);
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
        b.Property(x => x.OperationClaimId).IsRequired();
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.ExpiresAtUtc).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.AcceptedAtUtc);
        b.Property(x => x.AcceptedByUserId);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Email });
        b.HasIndex(x => x.TenantId);

        b.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
