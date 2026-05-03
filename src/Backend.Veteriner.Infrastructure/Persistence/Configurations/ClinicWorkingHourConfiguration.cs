using Backend.Veteriner.Domain.Clinics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ClinicWorkingHourConfiguration : IEntityTypeConfiguration<ClinicWorkingHour>
{
    public void Configure(EntityTypeBuilder<ClinicWorkingHour> b)
    {
        b.ToTable("ClinicWorkingHours");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ClinicId).IsRequired();
        b.Property(x => x.DayOfWeek).IsRequired();
        b.Property(x => x.IsClosed).IsRequired();
        b.Property(x => x.OpensAt);
        b.Property(x => x.ClosesAt);
        b.Property(x => x.BreakStartsAt);
        b.Property(x => x.BreakEndsAt);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        b.HasIndex(x => new { x.TenantId, x.ClinicId });
        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.DayOfWeek }).IsUnique();

        b.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(x => x.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
