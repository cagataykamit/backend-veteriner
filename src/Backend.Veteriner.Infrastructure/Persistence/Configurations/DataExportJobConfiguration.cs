using Backend.Veteriner.Domain.DataExports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Tüm veri dışa aktarma işi. Kiracıda tek açık iş veritabanı düzeyinde güvence altındadır: ikinci açık iş filtreli benzersiz
/// indekse çarpar. Filtre açık durum kümesinden (domain) üretilir.
/// </summary>
public sealed class DataExportJobConfiguration : IEntityTypeConfiguration<DataExportJob>
{
    public void Configure(EntityTypeBuilder<DataExportJob> b)
    {
        b.ToTable("DataExportJobs");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.RequestedByUserId).IsRequired();
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.RequestedAtUtc).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(200).IsUnicode(false);
        b.Property(x => x.LeaseOwner).HasMaxLength(DataExportJob.MaxLeaseOwnerLength);
        b.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        b.Ignore(x => x.IsOpen);

        b.HasIndex(x => new { x.TenantId, x.RequestedAtUtc });
        b.HasIndex(x => new { x.Status, x.ExpiresAtUtc });

        var open = string.Join(", ", DataExportJob.OpenStatuses.Select(s => (int)s));
        b.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasFilter($"[Status] IN ({open})")
            .HasDatabaseName(DataExportJob.OpenJobIndexName);
    }
}
