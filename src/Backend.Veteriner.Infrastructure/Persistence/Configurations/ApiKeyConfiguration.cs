using Backend.Veteriner.Domain.ExternalApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> b)
    {
        b.ToTable("ApiKeys", t =>
        {
            t.HasCheckConstraint("CK_ApiKeys_SecretHash_Length", $"DATALENGTH([SecretHash]) = {ApiKey.SecretHashLength}");
            t.HasCheckConstraint("CK_ApiKeys_Revoke_Consistent", "([RevokedAtUtc] IS NULL AND [RevokedByUserId] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevokedByUserId] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(ApiKey.MaxNameLength);
        b.Property(x => x.Prefix).IsRequired().HasMaxLength(ApiKey.PrefixLength).IsUnicode(false);
        b.Property(x => x.SecretHash).IsRequired().HasMaxLength(ApiKey.SecretHashLength);
        b.Property(x => x.Scopes).IsRequired().HasMaxLength(ApiKey.MaxScopesLength).IsUnicode(false);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.LastUsedAtUtc);
        b.Property(x => x.RevokedAtUtc);
        b.Property(x => x.RevokedByUserId);
        b.Ignore(x => x.ScopeList);

        // Kimlik doğrulama anahtarı ön ekle (kiracılar arası) bulur; ön ek tüm kiracılarda benzersiz.
        b.HasIndex(x => x.Prefix).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
    }
}
