using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class PaymentReadModelConfiguration : IEntityTypeConfiguration<PaymentReadModel>
{
    public void Configure(EntityTypeBuilder<PaymentReadModel> b)
    {
        b.ToTable("PaymentReadModels");
        b.HasKey(x => x.PaymentId);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.ClientId).IsRequired();
        b.Property(x => x.ClientName).IsRequired().HasMaxLength(QueryReadModelConstraints.ClientName);
        b.Property(x => x.ClientNameNormalized).IsRequired()
            .HasMaxLength(QueryReadModelConstraints.ClientNameNormalized);
        b.Property(x => x.PetName).HasMaxLength(QueryReadModelConstraints.PetName);
        b.Property(x => x.PetNameNormalized).HasMaxLength(QueryReadModelConstraints.PetNameNormalized);
        b.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(x => x.Currency).IsRequired().HasMaxLength(3);
        b.Property(x => x.Method).IsRequired();
        b.Property(x => x.PaidAtUtc).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(QueryReadModelConstraints.PaymentNotes);
        b.Property(x => x.NotesNormalized).HasMaxLength(QueryReadModelConstraints.PaymentNotesNormalized);
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastEventOccurredAtUtc).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.PaidAtUtc, x.PaymentId })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("IX_PaymentReadModels_TenantId_ClinicId_PaidAtUtc_PaymentId");

        b.HasIndex(x => new { x.TenantId, x.ClientId, x.PaidAtUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_PaymentReadModels_TenantId_ClientId_PaidAtUtc");

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ClientNameNormalized })
            .HasDatabaseName("IX_PaymentReadModels_TenantId_ClinicId_ClientNameNormalized");

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.PetNameNormalized })
            .HasDatabaseName("IX_PaymentReadModels_TenantId_ClinicId_PetNameNormalized");
    }
}
