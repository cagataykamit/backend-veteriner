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
        b.Property(x => x.Findings).IsRequired().HasMaxLength(8000);
        b.Property(x => x.Assessment).HasMaxLength(4000);
        b.Property(x => x.Notes).HasMaxLength(4000);

        b.HasIndex(x => x.TenantId);
        b.HasIndex(x => new { x.TenantId, x.ExaminedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.PetId });
        b.HasIndex(x => new { x.TenantId, x.AppointmentId });
    }
}
