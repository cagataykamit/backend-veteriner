using Backend.Veteriner.Domain.DrugGuide;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class LicensedProductConfiguration : IEntityTypeConfiguration<LicensedProduct>
{
    public void Configure(EntityTypeBuilder<LicensedProduct> b)
    {
        b.ToTable("LicensedProducts");

        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(LicensedProduct.NameMaxLength);
        b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(LicensedProduct.NameMaxLength);
        b.Property(x => x.LicenseHolder).IsRequired().HasMaxLength(LicensedProduct.LicenseHolderMaxLength);
        b.Property(x => x.NormalizedLicenseHolder).IsRequired().HasMaxLength(LicensedProduct.LicenseHolderMaxLength);
        b.Property(x => x.LicenseNumber).HasMaxLength(LicensedProduct.LicenseNumberMaxLength);
        b.Property(x => x.SpcUrl).HasMaxLength(LicensedProduct.SpcUrlMaxLength);
        b.Property(x => x.Source).IsRequired().HasMaxLength(LicensedProduct.SourceMaxLength);
        b.Property(x => x.SourceSyncedAtUtc).IsRequired();
        b.Property(x => x.IsListedInSource).IsRequired();

        b.HasMany(x => x.Ingredients)
            .WithOne()
            .HasForeignKey(i => i.LicensedProductId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Ingredients).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(x => x.TargetSpecies)
            .WithOne()
            .HasForeignKey(t => t.LicensedProductId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.TargetSpecies).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.NormalizedName);
        b.HasIndex(x => x.NormalizedLicenseHolder);
    }
}
