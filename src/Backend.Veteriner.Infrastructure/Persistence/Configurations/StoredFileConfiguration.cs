using Backend.Veteriner.Domain.Files;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> b)
    {
        b.ToTable("StoredFiles");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.OwnerType).IsRequired();
        b.Property(x => x.OwnerId).IsRequired();
        b.Property(x => x.StorageKey).IsRequired().HasMaxLength(StoredFile.StorageKeyMaxLength).IsUnicode(false);
        b.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(StoredFileName.MaxLength);
        b.Property(x => x.ContentType).IsRequired().HasMaxLength(StoredFile.ContentTypeMaxLength).IsUnicode(false);
        b.Property(x => x.SizeBytes).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(StoredFile.Sha256HexLength).IsFixedLength().IsUnicode(false);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.UploadedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ReadyAtUtc);
        b.Property(x => x.DeletedAtUtc);
        b.Property(x => x.DeletedByUserId);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.Type);

        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.OwnerType, x.OwnerId });
        b.HasIndex(x => new { x.Status, x.CreatedAtUtc });
    }
}
