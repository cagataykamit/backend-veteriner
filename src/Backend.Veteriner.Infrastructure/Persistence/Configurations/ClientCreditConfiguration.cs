using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClientCreditConfiguration : IEntityTypeConfiguration<ClientCredit>
{
    public void Configure(EntityTypeBuilder<ClientCredit> b)
    {
        b.ToTable("ClientCredits", t =>
        {
            t.HasCheckConstraint("CK_ClientCredits_Amount_Positive", "[Amount] > 0");
            t.HasCheckConstraint("CK_ClientCredits_UsedAmount_Range", "[UsedAmount] >= 0 AND [UsedAmount] <= [Amount]");
            t.HasCheckConstraint(
                "CK_ClientCredits_Refund_Consistent",
                "([RefundedAtUtc] IS NULL AND [RefundedAmount] IS NULL AND [RefundedByUserId] IS NULL) OR ([RefundedAtUtc] IS NOT NULL AND [RefundedAmount] > 0 AND [RefundedByUserId] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.SourcePaymentId).IsRequired();
        b.Property(x => x.Source).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.UsedAmount).HasPrecision(18, 2).IsRequired().HasDefaultValue(0m);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.RefundedAtUtc);
        b.Property(x => x.RefundedByUserId);
        b.Property(x => x.RefundedAmount).HasPrecision(18, 2);
        b.Property(x => x.RefundMethod);
        b.Property(x => x.RefundReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();

        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.SourcePaymentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Client>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.ClientId, x.ClinicId });

        // Bir ödeme en fazla bir kredi üretir (FIN-005 K1).
        b.HasIndex(x => new { x.TenantId, x.SourcePaymentId }).IsUnique();
    }
}

public sealed class ClientCreditApplicationConfiguration : IEntityTypeConfiguration<ClientCreditApplication>
{
    public void Configure(EntityTypeBuilder<ClientCreditApplication> b)
    {
        b.ToTable("ClientCreditApplications", t =>
        {
            t.HasCheckConstraint("CK_ClientCreditApplications_Amount_Positive", "[Amount] > 0");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.CreditId).IsRequired();
        b.Property(x => x.ChargeId).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.RequestId);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.ReversedAtUtc);
        b.Property(x => x.ReversedByUserId);
        b.Property(x => x.ReverseReason).HasMaxLength(CorrectionReason.MaxLength);

        b.HasOne<ClientCredit>().WithMany().HasForeignKey(x => x.CreditId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Charge>().WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.TenantId, x.CreditId });
        b.HasIndex(x => new { x.TenantId, x.ChargeId });
        b.HasIndex(x => new { x.TenantId, x.RequestId }).HasFilter("[RequestId] IS NOT NULL");
    }
}
