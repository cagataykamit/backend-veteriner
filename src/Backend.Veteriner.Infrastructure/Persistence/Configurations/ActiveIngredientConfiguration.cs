using Backend.Veteriner.Domain.DrugGuide;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class ActiveIngredientConfiguration : IEntityTypeConfiguration<ActiveIngredient>
{
    public void Configure(EntityTypeBuilder<ActiveIngredient> b)
    {
        b.ToTable("ActiveIngredients");

        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(ActiveIngredient.NameMaxLength);
        b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(ActiveIngredient.NameMaxLength);

        // Yazım varyantları tek maddedir.
        b.HasIndex(x => x.NormalizedName).IsUnique();
    }
}
