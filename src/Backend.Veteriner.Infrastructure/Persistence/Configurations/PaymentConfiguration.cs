using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    /// <summary>Aynı istemci isteği (RequestId) kiracıda en fazla bir ödeme üretir.</summary>
    public const string RequestIdIndexName = "UX_Payments_TenantId_RequestId";

    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_AllocatedAmount_Range", "[AllocatedAmount] >= 0 AND [AllocatedAmount] <= [Amount]");
            t.HasCheckConstraint("CK_Payments_Void_Consistent", "([VoidedAtUtc] IS NULL) = ([VoidReason] IS NULL) AND ([VoidedAtUtc] IS NULL) = ([VoidedByUserId] IS NULL)");
            t.HasCheckConstraint("CK_Payments_Voided_Unallocated", "[VoidedAtUtc] IS NULL OR [AllocatedAmount] = 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.PetId);
        b.Property(x => x.AppointmentId);
        b.Property(x => x.ExaminationId);

        b.Property(x => x.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.Method).IsRequired();
        b.Property(x => x.PaidAtUtc).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(4000);

        b.Property(x => x.AllocatedAmount).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
        b.Property(x => x.RequestId);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();

        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidedByUserId);
        b.Property(x => x.VoidReason).HasMaxLength(CorrectionReason.MaxLength);

        // İptal edilen ödeme tüm okuma yüzeylerinden tek yerde düşer; iptal ucu ve requestId araması IgnoreQueryFilters kullanır.
        b.HasQueryFilter(x => x.VoidedAtUtc == null);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.ClientId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.PaidAtUtc });
        b.HasIndex(x => new { x.TenantId, x.Method });

        b.HasIndex(x => new { x.TenantId, x.RequestId })
            .IsUnique()
            .HasFilter("[RequestId] IS NOT NULL")
            .HasDatabaseName(RequestIdIndexName);
    }
}
