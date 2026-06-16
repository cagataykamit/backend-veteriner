using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class ClinicClientActivityReadModelConfiguration : IEntityTypeConfiguration<ClinicClientActivityReadModel>
{
    public void Configure(EntityTypeBuilder<ClinicClientActivityReadModel> b)
    {
        b.ToTable("ClinicClientActivityReadModels");
        b.HasKey(x => new { x.TenantId, x.ClinicId, x.ClientId });

        b.Property(x => x.ClientName).IsRequired().HasMaxLength(QueryReadModelConstraints.ClientName);
        b.Property(x => x.ClientPhone).HasMaxLength(QueryReadModelConstraints.ClientPhone);
        b.Property(x => x.LastAppointmentAtUtc).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.LastAppointmentAtUtc, x.ClientId })
            .IsDescending(false, false, true, false)
            .HasDatabaseName("IX_ClinicClientActivityReadModels_TenantId_ClinicId_LastAppointmentAtUtc_ClientId");
    }
}
