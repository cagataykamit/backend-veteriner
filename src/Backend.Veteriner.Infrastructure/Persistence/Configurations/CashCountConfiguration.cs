using Backend.Veteriner.Application.CashCounts;
using Backend.Veteriner.Domain.CashCounts;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class CashCountConfiguration : IEntityTypeConfiguration<CashCount>
{
    public void Configure(EntityTypeBuilder<CashCount> b)
    {
        b.ToTable("CashCounts", t =>
        {
            t.HasCheckConstraint("CK_CashCounts_Amounts_NonNegative", "[OpeningAmount] >= 0 AND [CountedAmount] >= 0");
            t.HasCheckConstraint("CK_CashCounts_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.CashRegisterId).IsRequired();
        b.Property(x => x.BusinessDate).IsRequired();
        b.Property(x => x.OpeningAmount).HasPrecision(18, CashCount.AmountDecimals).IsRequired();
        b.Property(x => x.ExpectedCashAmount).HasPrecision(18, CashCount.AmountDecimals).IsRequired();
        b.Property(x => x.CountedAmount).HasPrecision(18, CashCount.AmountDecimals).IsRequired();
        b.Property(x => x.Note).HasMaxLength(CashCount.MaxNoteLength);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.CorrectsCashCountId);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidedByUserId);
        b.Property(x => x.VoidReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();

        // Kasa ve gün başına en çok bir geçerli sayım; iki kullanıcı aynı anda kaydederse ikincisi burada düşer.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CashRegisterId, x.BusinessDate })
            .IsUnique()
            .HasFilter("[VoidedAtUtc] IS NULL")
            .HasDatabaseName(CashCountErrors.ActivePerDayIndexName);

        // Geçmiş listesi ve günün düzeltme izi iptal edilenleri de okur.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.BusinessDate, x.CreatedAtUtc });

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.CashRegisterId).OnDelete(DeleteBehavior.Restrict);
    }
}
