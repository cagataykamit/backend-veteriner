using Backend.Veteriner.Domain.DrugGuide;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class LicensedProductIngredientConfiguration : IEntityTypeConfiguration<LicensedProductIngredient>
{
    public void Configure(EntityTypeBuilder<LicensedProductIngredient> b)
    {
        b.ToTable("LicensedProductIngredients");

        b.HasKey(x => new { x.LicensedProductId, x.ActiveIngredientId });
        b.Property(x => x.SortOrder).IsRequired();

        b.HasOne(x => x.Ingredient)
            .WithMany()
            .HasForeignKey(x => x.ActiveIngredientId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.ActiveIngredientId);
    }
}
