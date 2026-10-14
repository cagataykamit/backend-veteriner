using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AdministrationConfiguration : IEntityTypeConfiguration<Administration>
{
    /// <summary>Aynı istemci isteği (RequestId) kiracıda en fazla bir uygulama üretir.</summary>
    public const string RequestIdIndexName = "UX_Administrations_TenantId_RequestId";

    /// <summary>Bir aşı kaydı en çok bir uygulamaya bağlanır.</summary>
    public const string VaccinationIndexName = "UX_Administrations_TenantId_VaccinationId";

    public void Configure(EntityTypeBuilder<Administration> b)
    {
        b.ToTable("Administrations", t =>
        {
            t.HasCheckConstraint("CK_Administrations_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_Administrations_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL AND [VoidStockOutcome] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL AND [VoidStockOutcome] IS NOT NULL)");
            t.HasCheckConstraint("CK_Administrations_LateEntry_Consistent", "([AddedAfterFinalization] = 0 AND [LateEntryReason] IS NULL) OR ([AddedAfterFinalization] = 1 AND [LateEntryReason] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.ExaminationId);
        b.Property(x => x.TreatmentId);
        b.Property(x => x.ProductId).IsRequired();

        b.Property(x => x.Quantity).HasPrecision(18, Administration.QuantityDecimals).IsRequired();
        b.Property(x => x.AdministeredAtUtc).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(Administration.MaxNotesLength);

        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidedByUserId);
        b.Property(x => x.VoidReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.VoidStockOutcome);

        b.Property(x => x.AddedAfterFinalization).IsRequired();
        b.Property(x => x.LateEntryReason).HasMaxLength(CorrectionReason.MaxLength);

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RequestId);
        b.Property(x => x.BatchId);
        b.Property(x => x.VaccinationId);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => new { x.TenantId, x.TreatmentId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.AdministeredAtUtc });

        b.HasOne<StockBatch>()
            .WithMany()
            .HasForeignKey(x => x.BatchId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<Vaccination>()
            .WithMany()
            .HasForeignKey(x => x.VaccinationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.VaccinationId })
            .IsUnique()
            .HasFilter("[VaccinationId] IS NOT NULL")
            .HasDatabaseName(VaccinationIndexName);

        b.HasIndex(x => new { x.TenantId, x.RequestId })
            .IsUnique()
            .HasFilter("[RequestId] IS NOT NULL")
            .HasDatabaseName(RequestIdIndexName);
    }
}
