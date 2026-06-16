using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class AppointmentReadModelConfiguration : IEntityTypeConfiguration<AppointmentReadModel>
{
    public void Configure(EntityTypeBuilder<AppointmentReadModel> b)
    {
        b.ToTable("AppointmentReadModels");
        b.HasKey(x => x.AppointmentId);

        b.Property(x => x.ClinicName).IsRequired().HasMaxLength(QueryReadModelConstraints.ClinicName);
        b.Property(x => x.PetName).IsRequired().HasMaxLength(QueryReadModelConstraints.PetName);
        b.Property(x => x.SpeciesName).IsRequired().HasMaxLength(QueryReadModelConstraints.SpeciesName);
        b.Property(x => x.ClientName).IsRequired().HasMaxLength(QueryReadModelConstraints.ClientName);
        b.Property(x => x.ClientPhone).HasMaxLength(QueryReadModelConstraints.ClientPhone);
        b.Property(x => x.Notes).HasMaxLength(QueryReadModelConstraints.Notes);
        b.Property(x => x.ScheduledAtUtc).IsRequired();
        b.Property(x => x.ScheduledEndUtc).IsRequired();
        b.Property(x => x.DurationMinutes).IsRequired();
        b.Property(x => x.AppointmentType).IsRequired();
        b.Property(x => x.Status).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.ScheduledAtUtc, x.AppointmentId })
            .IsDescending(false, false, true, true)
            .HasDatabaseName("IX_AppointmentReadModels_TenantId_ClinicId_ScheduledAtUtc_AppointmentId");

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.Status, x.ScheduledAtUtc })
            .HasDatabaseName("IX_AppointmentReadModels_TenantId_ClinicId_Status_ScheduledAtUtc");

        b.HasIndex(x => new { x.TenantId, x.PetId })
            .HasDatabaseName("IX_AppointmentReadModels_TenantId_PetId");

        b.HasIndex(x => new { x.TenantId, x.ClientId })
            .HasDatabaseName("IX_AppointmentReadModels_TenantId_ClientId");
    }
}
