using Backend.Veteriner.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Kaynak kimliği bağı (ADR-051). Benzersiz anahtar okuma yolunun da indeksidir: doğrulama ve uygulama
/// <c>(TenantId, ClinicId, SourceProfile, Entity, SourceKey IN …)</c> sorar. Partiye yabancı anahtar yok: parti silinse de bağ kalır.
/// </summary>
public sealed class ImportSourceLinkConfiguration : IEntityTypeConfiguration<ImportSourceLink>
{
    public const string KeyIndexName = "UX_ImportSourceLinks_Key";

    public void Configure(EntityTypeBuilder<ImportSourceLink> b)
    {
        b.ToTable("ImportSourceLinks");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.SourceProfile).IsRequired();
        b.Property(x => x.Entity).IsRequired();
        b.Property(x => x.SourceKey).IsRequired().HasMaxLength(ImportSourceLink.MaxSourceKeyLength);
        b.Property(x => x.TargetId).IsRequired();
        b.Property(x => x.BatchId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.SourceProfile, x.Entity, x.SourceKey })
            .IsUnique()
            .HasDatabaseName(KeyIndexName);
    }
}
