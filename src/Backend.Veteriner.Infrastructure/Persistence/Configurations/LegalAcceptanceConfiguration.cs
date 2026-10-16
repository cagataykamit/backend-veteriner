using Backend.Veteriner.Domain.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Hukuki belge kabul kayıtları (ADR-029). Yalnız eklenir; kullanıcı/kiracı silinse de iz kalsın diye yabancı anahtar yoktur
/// (AuditLogs ile aynı yaklaşım). Kabul eden ya personel (UserId + TenantId) ya portal hesabıdır (OwnerAccountId; ADR-031).
/// </summary>
public sealed class LegalAcceptanceConfiguration : IEntityTypeConfiguration<LegalAcceptance>
{
    public void Configure(EntityTypeBuilder<LegalAcceptance> b)
    {
        b.ToTable("LegalAcceptances", t =>
        {
            t.HasCheckConstraint(
                "CK_LegalAcceptances_SingleAcceptor",
                "([UserId] IS NOT NULL AND [TenantId] IS NOT NULL AND [OwnerAccountId] IS NULL) "
                + "OR ([UserId] IS NULL AND [TenantId] IS NULL AND [OwnerAccountId] IS NOT NULL)");
        });
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId);
        b.Property(x => x.TenantId);
        b.Property(x => x.OwnerAccountId);
        b.Property(x => x.DocumentType).IsRequired();
        b.Property(x => x.Version).IsRequired().HasMaxLength(LegalAcceptance.MaxVersionLength);
        b.Property(x => x.AcceptedAtUtc).IsRequired();

        b.HasIndex(x => new { x.UserId, x.DocumentType });
        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.OwnerAccountId, x.DocumentType })
            .HasFilter("[OwnerAccountId] IS NOT NULL");
    }
}
