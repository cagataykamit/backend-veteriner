using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.DrugGuide;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class LicensedProductTargetSpeciesConfiguration : IEntityTypeConfiguration<LicensedProductTargetSpecies>
{
    public void Configure(EntityTypeBuilder<LicensedProductTargetSpecies> b)
    {
        b.ToTable("LicensedProductTargetSpecies");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Label).IsRequired().HasMaxLength(LicensedProductTargetSpecies.LabelMaxLength);

        b.HasOne<Species>()
            .WithMany()
            .HasForeignKey(x => x.SpeciesId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.LicensedProductId);
    }
}
