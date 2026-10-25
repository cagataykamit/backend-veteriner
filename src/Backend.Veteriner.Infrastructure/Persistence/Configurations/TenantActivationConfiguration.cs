using Backend.Veteriner.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Kiracı başına tek aktivasyon kaydı (TRIAL-008); kiracı silinirse kayıt da silinir.</summary>
public sealed class TenantActivationConfiguration : IEntityTypeConfiguration<TenantActivation>
{
    public void Configure(EntityTypeBuilder<TenantActivation> b)
    {
        b.ToTable("TenantActivations");

        b.HasKey(x => x.TenantId);

        b.Property(x => x.ExampleClinicLoadedAtUtc);
        b.Property(x => x.FirstActionAtUtc);
        b.Property(x => x.FirstActionKind).HasConversion<int?>();
        b.Property(x => x.FirstActionUserId);

        b.HasOne<Tenant>()
            .WithOne()
            .HasForeignKey<TenantActivation>(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
