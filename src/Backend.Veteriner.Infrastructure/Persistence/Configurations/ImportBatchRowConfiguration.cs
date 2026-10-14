using Backend.Veteriner.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Hazırlık satırları; kiracı erişimi her zaman partinin kiracı denetiminden geçer. Satırlar parti ile birlikte silinir.
/// </summary>
public sealed class ImportBatchRowConfiguration : IEntityTypeConfiguration<ImportBatchRow>
{
    public const string RowNumberIndexName = "UX_ImportBatchRows_BatchId_Table_RowNumber";

    public void Configure(EntityTypeBuilder<ImportBatchRow> b)
    {
        b.ToTable("ImportBatchRows");

        b.HasKey(x => x.Id);

        b.Property(x => x.BatchId).IsRequired();
        b.Property(x => x.Table).IsRequired();
        b.Property(x => x.RowNumber).IsRequired();
        b.Property(x => x.SourceJson);
        b.Property(x => x.NormalizedJson);
        b.Property(x => x.ValidationStatus).IsRequired();
        b.Property(x => x.IssuesJson);
        b.Property(x => x.ApplyStatus).IsRequired();
        b.Property(x => x.TargetEntityType).HasMaxLength(ImportBatchRow.MaxTargetEntityTypeLength);
        b.Property(x => x.TargetEntityId);
        b.Property(x => x.Matched).IsRequired();
        b.Property(x => x.AppliedAtUtc);

        b.HasOne<ImportBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.BatchId, x.Table, x.RowNumber })
            .IsUnique()
            .HasDatabaseName(RowNumberIndexName);

        // Geri alma: oluşturulan kayıttan satıra ulaşma.
        b.HasIndex(x => x.TargetEntityId).HasFilter("[TargetEntityId] IS NOT NULL");
    }
}
