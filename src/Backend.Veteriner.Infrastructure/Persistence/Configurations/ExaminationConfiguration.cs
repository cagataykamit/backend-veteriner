using Backend.Veteriner.Domain.Examinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ExaminationConfiguration : IEntityTypeConfiguration<Examination>
{
    public void Configure(EntityTypeBuilder<Examination> b)
    {
        b.ToTable("Examinations");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.PetId).IsRequired();
        b.Property(x => x.AppointmentId);
        b.Property(x => x.ExaminedAtUtc).IsRequired();

        b.Property(x => x.VisitReason).IsRequired().HasMaxLength(2000);
        b.Property(x => x.Anamnesis).HasMaxLength(4000);
        b.Property(x => x.Findings).IsRequired().HasMaxLength(8000);
        b.Property(x => x.WeightKg).HasPrecision(9, 3);
        b.Property(x => x.TemperatureC).HasPrecision(5, 2);
        b.Property(x => x.HeartRateBpm);
        b.Property(x => x.RespiratoryRatePerMin);
        b.Property(x => x.VitalsMeasuredAtUtc);
        b.Property(x => x.Assessment).HasMaxLength(4000);
        b.Property(x => x.Plan).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.Property(x => x.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ExaminedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.AppointmentId });
    }
}
