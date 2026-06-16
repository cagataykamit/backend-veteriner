using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Query.Configurations;

public sealed class ClinicPetActivityReadModelConfiguration : IEntityTypeConfiguration<ClinicPetActivityReadModel>
{
    public void Configure(EntityTypeBuilder<ClinicPetActivityReadModel> b)
    {
        b.ToTable("ClinicPetActivityReadModels");
        b.HasKey(x => new { x.TenantId, x.ClinicId, x.PetId });

        b.Property(x => x.PetName).IsRequired().HasMaxLength(QueryReadModelConstraints.PetName);
        b.Property(x => x.SpeciesName).IsRequired().HasMaxLength(QueryReadModelConstraints.SpeciesName);
        b.Property(x => x.LastAppointmentAtUtc).IsRequired();
        b.Property(x => x.LastEventId).IsRequired();
        b.Property(x => x.LastProjectedAtUtc).IsRequired();

        b.HasIndex(x => new { x.TenantId, x.ClinicId, x.LastAppointmentAtUtc, x.PetId })
            .IsDescending(false, false, true, false)
            .HasDatabaseName("IX_ClinicPetActivityReadModels_TenantId_ClinicId_LastAppointmentAtUtc_PetId");
    }
}
