using Backend.Veteriner.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");

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

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.ClientId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.PaidAtUtc });
        b.HasIndex(x => new { x.TenantId, x.Method });
    }
}
