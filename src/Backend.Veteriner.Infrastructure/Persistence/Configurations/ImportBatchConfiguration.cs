using Backend.Veteriner.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// İçe aktarma partisi. Idempotency ve eşzamanlılık veritabanı düzeyinde güvence altındadır: aynı istek, aynı aktif dosya
/// ve kiracıda ikinci aktif iş filtreli benzersiz indekslere çarpar. Filtreler durum kümelerinden (domain) üretilir.
/// </summary>
public sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public const string RequestIdIndexName = "UX_ImportBatches_TenantId_RequestId";
    public const string ContentHashIndexName = "UX_ImportBatches_TenantId_ContentHash";
    public const string ActiveJobIndexName = "UX_ImportBatches_TenantId_ActiveJob";

    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("ImportBatches");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId);
        b.Property(x => x.RequestId).IsRequired();
        b.Property(x => x.ContentHash).HasMaxLength(ImportBatch.ContentHashLength).IsUnicode(false);
        b.Property(x => x.Name).HasMaxLength(ImportBatch.MaxNameLength);
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.MappingJson);
        b.Property(x => x.OptionsJson);
        b.Property(x => x.ValidationId);

        // Sayaçlar tablo başına sütun grubu: CustomerCounts_Total, PetCounts_Created, ...
        b.ComplexProperty(x => x.CustomerCounts).IsRequired();
        b.ComplexProperty(x => x.PetCounts).IsRequired();
        b.ComplexProperty(x => x.VaccinationCounts).IsRequired();

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);
        b.Property(x => x.ValidatedAtUtc);
        b.Property(x => x.CommittedAtUtc);
        b.Property(x => x.RolledBackAtUtc);
        b.Property(x => x.RemindersReleasedAtUtc);
        b.Property(x => x.PayloadPurgedAtUtc);
        b.Property(x => x.LeaseOwner).HasMaxLength(ImportBatch.MaxLeaseOwnerLength);
        b.Property(x => x.LeaseUntilUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.Ignore(x => x.IsDraft);

        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });

        b.HasIndex(x => new { x.TenantId, x.RequestId })
            .IsUnique()
            .HasDatabaseName(RequestIdIndexName);

        b.HasIndex(x => new { x.TenantId, x.ContentHash })
            .IsUnique()
            .HasFilter($"[ContentHash] IS NOT NULL AND {StatusNotIn(ImportBatchStatusSets.ContentHashReleased)}")
            .HasDatabaseName(ContentHashIndexName);

        b.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter($"[Status] IN ({StatusList(ImportBatchStatusSets.ActiveJob)})")
            .HasDatabaseName(ActiveJobIndexName);
    }

    private static string StatusList(IEnumerable<ImportBatchStatus> statuses)
        => string.Join(", ", statuses.Select(s => (int)s));

    /// <summary>Filtreli indeks yüklemi <c>NOT IN</c> desteklemez; eşitsizlik zinciri üretilir.</summary>
    private static string StatusNotIn(IEnumerable<ImportBatchStatus> statuses)
        => string.Join(" AND ", statuses.Select(s => $"[Status] <> {(int)s}"));
}
