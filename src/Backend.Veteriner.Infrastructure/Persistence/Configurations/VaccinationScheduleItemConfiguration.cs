using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class VaccinationScheduleItemConfiguration : IEntityTypeConfiguration<VaccinationScheduleItem>
{
    public void Configure(EntityTypeBuilder<VaccinationScheduleItem> b)
    {
        b.ToTable("VaccinationScheduleItems");

        b.HasKey(x => x.Id);

        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.SpeciesId).IsRequired();
        b.Property(x => x.VaccineDefinitionId).IsRequired();
        b.Property(x => x.StartAgeWeeks).IsRequired();
        b.Property(x => x.DoseCount).IsRequired();
        b.Property(x => x.DoseIntervalWeeks);
        b.Property(x => x.RepeatIntervalMonths);
        b.Property(x => x.Notes).HasMaxLength(VaccinationScheduleItem.NotesMaxLength);
        b.Property(x => x.CreatedAtUtc).IsRequired();
        b.Property(x => x.UpdatedAtUtc);

        // Kiracı başına tür + aşı tek satır; öneri hayvanın türüyle okunur.
        b.HasIndex(x => new { x.TenantId, x.SpeciesId, x.VaccineDefinitionId }).IsUnique();

        b.HasOne<Species>().WithMany().HasForeignKey(x => x.SpeciesId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VaccineDefinition>().WithMany().HasForeignKey(x => x.VaccineDefinitionId).OnDelete(DeleteBehavior.Restrict);
    }
}
