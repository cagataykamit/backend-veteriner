using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PaymentAllocationConfiguration : IEntityTypeConfiguration<PaymentAllocation>
{
    public void Configure(EntityTypeBuilder<PaymentAllocation> b)
    {
        b.ToTable("PaymentAllocations", t =>
        {
            t.HasCheckConstraint("CK_PaymentAllocations_Amount_Positive", "[Amount] > 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PaymentId).IsRequired();
        b.Property(x => x.ChargeId).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ReversedAtUtc);

        // Geri alınan (iptal edilen ödemenin) tahsisleri geçmişte kalır, aktif tahsis sorgularında görünmez.
        b.HasQueryFilter(x => x.ReversedAtUtc == null);

        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Charge>().WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.PaymentId });
        b.HasIndex(x => new { x.TenantId, x.ChargeId });
    }
}
