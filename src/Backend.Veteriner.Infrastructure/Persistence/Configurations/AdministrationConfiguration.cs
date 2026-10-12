using Backend.Veteriner.Domain.Administrations;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class AdministrationConfiguration : IEntityTypeConfiguration<Administration>
{
    /// <summary>Aynı istemci isteği (RequestId) kiracıda en fazla bir uygulama üretir.</summary>
    public const string RequestIdIndexName = "UX_Administrations_TenantId_RequestId";

    public void Configure(EntityTypeBuilder<Administration> b)
    {
        b.ToTable("Administrations", t =>
        {
            t.HasCheckConstraint("CK_Administrations_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_Administrations_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");
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

        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RequestId);

        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.ExaminationId });
        b.HasIndex(x => new { x.TenantId, x.TreatmentId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.AdministeredAtUtc });

        b.HasIndex(x => new { x.TenantId, x.RequestId })
            .IsUnique()
            .HasFilter("[RequestId] IS NOT NULL")
            .HasDatabaseName(RequestIdIndexName);
    }
}
