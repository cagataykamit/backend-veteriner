using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

public sealed class PetColorConfiguration : IEntityTypeConfiguration<PetColor>
{
    public void Configure(EntityTypeBuilder<PetColor> b)
    {
        b.ToTable("PetColors");

        b.HasKey(x => x.Id);

        b.Property(x => x.Code)
            .IsRequired()
            .HasMaxLength(32);

        b.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        b.Property(x => x.IsActive)
            .IsRequired();

        b.Property(x => x.DisplayOrder)
            .IsRequired();

        b.HasIndex(x => x.Code)
            .IsUnique();

        b.HasData(
            Seed(PetColorSeedConstants.Black, "BLACK", "Siyah", 1),
            Seed(PetColorSeedConstants.White, "WHITE", "Beyaz", 2),
            Seed(PetColorSeedConstants.Brown, "BROWN", "Kahverengi", 3),
            Seed(PetColorSeedConstants.Gray, "GRAY", "Gri", 4),
            Seed(PetColorSeedConstants.Yellow, "YELLOW", "Sarı", 5),
            Seed(PetColorSeedConstants.Cream, "CREAM", "Krem", 6),
            Seed(PetColorSeedConstants.Red, "RED", "Kızıl", 7),
            Seed(PetColorSeedConstants.Orange, "ORANGE", "Turuncu", 8),
            Seed(PetColorSeedConstants.BlackWhite, "BLACK_WHITE", "Siyah-Beyaz", 9),
            Seed(PetColorSeedConstants.BrownWhite, "BROWN_WHITE", "Kahverengi-Beyaz", 10),
            Seed(PetColorSeedConstants.GrayWhite, "GRAY_WHITE", "Gri-Beyaz", 11),
            Seed(PetColorSeedConstants.Spotted, "SPOTTED", "Benekli", 12),
            Seed(PetColorSeedConstants.Striped, "STRIPED", "Çizgili", 13),
            Seed(PetColorSeedConstants.Calico, "CALICO", "Alacalı", 14),
            Seed(PetColorSeedConstants.MultiColor, "MULTI_COLOR", "Çok Renkli", 15),
            Seed(PetColorSeedConstants.Other, "OTHER", "Diğer", 16),
            Seed(PetColorSeedConstants.Unknown, "UNKNOWN", "Bilinmiyor", 17));
    }

    private static PetColor Seed(Guid id, string code, string name, int displayOrder)
    {
        var entity = new PetColor(code, name, displayOrder);
        typeof(PetColor).GetProperty(nameof(PetColor.Id))!.SetValue(entity, id);
        return entity;
    }
}
