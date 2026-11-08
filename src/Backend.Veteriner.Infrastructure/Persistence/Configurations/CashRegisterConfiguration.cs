using Backend.Veteriner.Application.CashRegisters;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class CashRegisterConfiguration : IEntityTypeConfiguration<CashRegister>
{
    public void Configure(EntityTypeBuilder<CashRegister> b)
    {
        b.ToTable("CashRegisters", t =>
            t.HasCheckConstraint("CK_CashRegisters_Main_Active", "[IsMain] = 0 OR [IsActive] = 1"));

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.Code).IsRequired().HasMaxLength(CashRegister.MaxCodeLength);
        b.Property(x => x.Name).IsRequired().HasMaxLength(CashRegister.MaxNameLength);
        b.Property(x => x.IsMain).IsRequired();
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        // Klinikte kod benzersiz; klinik başına tek Ana kasa.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Code })
            .IsUnique()
            .HasDatabaseName(CashRegisterMapping.CodeIndexName);
        b.HasIndex(x => new { x.TenantId, x.ClinicId })
            .IsUnique()
            .HasFilter("[IsMain] = 1")
            .HasDatabaseName(CashRegisterMapping.MainIndexName);

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CashMovementCategoryConfiguration : IEntityTypeConfiguration<CashMovementCategory>
{
    public void Configure(EntityTypeBuilder<CashMovementCategory> b)
    {
        b.ToTable("CashMovementCategories");
        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Name).IsRequired().HasMaxLength(CashMovementCategory.MaxNameLength);
        b.Property(x => x.IsActive).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.Name })
            .IsUnique()
            .HasDatabaseName(CashRegisterMapping.CategoryNameIndexName);
    }
}

public sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> b)
    {
        b.ToTable("CashMovements", t =>
        {
            t.HasCheckConstraint("CK_CashMovements_Amount_Positive", "[Amount] > 0");
            t.HasCheckConstraint("CK_CashMovements_Void_Consistent", "([VoidedAtUtc] IS NULL AND [VoidReason] IS NULL AND [VoidedByUserId] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidReason] IS NOT NULL AND [VoidedByUserId] IS NOT NULL)");
            t.HasCheckConstraint("CK_CashMovements_Transfer_Consistent", "([Kind] IN (1, 2) AND [TransferId] IS NULL AND [CounterpartCashRegisterId] IS NULL) OR ([Kind] IN (3, 4) AND [TransferId] IS NOT NULL AND [CounterpartCashRegisterId] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.CashRegisterId).IsRequired();
        b.Property(x => x.Kind).IsRequired();
        b.Property(x => x.BusinessDate).IsRequired();
        b.Property(x => x.Amount).HasPrecision(18, CashMovement.AmountDecimals).IsRequired();
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.CategoryId);
        b.Property(x => x.Description).HasMaxLength(CashMovement.MaxDescriptionLength);
        b.Property(x => x.TransferId);
        b.Property(x => x.CounterpartCashRegisterId);
        b.Property(x => x.CreatedByUserId).IsRequired();
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.VoidedAtUtc);
        b.Property(x => x.VoidedByUserId);
        b.Property(x => x.VoidReason).HasMaxLength(CorrectionReason.MaxLength);
        b.Property(x => x.MutationSequence).IsRequired().HasDefaultValue(0L).IsConcurrencyToken();

        // Gün neti, devreden bakiye ve rapor kasa ve güne göre okur.
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.CashRegisterId, x.BusinessDate });
        b.HasIndex(x => new { x.TenantId, x.TransferId }).HasFilter("[TransferId] IS NOT NULL");

        b.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.CashRegisterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashRegister>().WithMany().HasForeignKey(x => x.CounterpartCashRegisterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CashMovementCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}
