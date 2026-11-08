using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>Kliniğin ticari ileti kapısı ve İYS beyanı (ADR-046 K2); klinik başına bir kayıt.</summary>
public sealed class ClinicMarketingSettingsConfiguration : IEntityTypeConfiguration<ClinicMarketingSettings>
{
    public void Configure(EntityTypeBuilder<ClinicMarketingSettings> b)
    {
        b.ToTable("ClinicMarketingSettings");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.IsEnabled).IsRequired();
        b.Property(x => x.DeclaredAtUtc);
        b.Property(x => x.DeclaredByUserId);
        b.Property(x => x.DeclarationVersion).HasMaxLength(ClinicMarketingSettings.DeclarationVersionMaxLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.UpdatedByUserId);

        b.HasIndex(x => new { x.TenantId, x.ClinicId }).IsUnique();

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Ticari iletideki ret bağlantısı (ADR-046 K3); ham token yalnız e-postada, burada SHA-256 özeti.</summary>
public sealed class MarketingOptOutLinkConfiguration : IEntityTypeConfiguration<MarketingOptOutLink>
{
    public void Configure(EntityTypeBuilder<MarketingOptOutLink> b)
    {
        b.ToTable("MarketingOptOutLinks");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.OwnerAccountClientLinkId).IsRequired();
        b.Property(x => x.TokenHash).IsRequired().HasMaxLength(MarketingOptOutLink.TokenHashLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UsedAtUtc);

        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.OwnerAccountClientLinkId });

        b.HasOne<OwnerAccountClientLink>()
            .WithMany()
            .HasForeignKey(x => x.OwnerAccountClientLinkId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
