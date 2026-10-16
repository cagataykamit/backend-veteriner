using Backend.Veteriner.Domain.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Hukuki belge kabul kayıtları (ADR-029). Yalnız eklenir; kullanıcı/kiracı silinse de iz kalsın diye yabancı anahtar yoktur
/// (AuditLogs ile aynı yaklaşım).
/// </summary>
public sealed class LegalAcceptanceConfiguration : IEntityTypeConfiguration<LegalAcceptance>
{
    public void Configure(EntityTypeBuilder<LegalAcceptance> b)
    {
        b.ToTable("LegalAcceptances");
        b.HasKey(x => x.Id);

        b.Property(x => x.UserId).IsRequired();
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.DocumentType).IsRequired();
        b.Property(x => x.Version).IsRequired().HasMaxLength(LegalAcceptance.MaxVersionLength);
        b.Property(x => x.AcceptedAtUtc).IsRequired();

        b.HasIndex(x => new { x.UserId, x.DocumentType });
        b.HasIndex(x => x.TenantId);
    }
}
