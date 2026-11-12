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
            Seed(PetColorSeedConstants.Piebald, "PIEBALD", "Alacalı", 14),
            Seed(PetColorSeedConstants.MultiColor, "MULTI_COLOR", "Çok Renkli", 15),
            Seed(PetColorSeedConstants.Brindle, "BRINDLE", "Kırçıllı", 20),
            Seed(PetColorSeedConstants.BrindleWhite, "BRINDLE_WHITE", "Kırçıllı-Beyaz", 21),
            Seed(PetColorSeedConstants.YellowWhite, "YELLOW_WHITE", "Sarı-Beyaz", 22),
            Seed(PetColorSeedConstants.BrownBlack, "BROWN_BLACK", "Kahverengi-Siyah", 23),
            Seed(PetColorSeedConstants.LightBrown, "LIGHT_BROWN", "Açık Kahverengi", 24),
            Seed(PetColorSeedConstants.Dun, "DUN", "Boz", 25),
            Seed(PetColorSeedConstants.Beige, "BEIGE", "Bej", 26),
            Seed(PetColorSeedConstants.Blue, "BLUE", "Mavi", 27),
            Seed(PetColorSeedConstants.BlackYellow, "BLACK_YELLOW", "Siyah-Sarı", 28),
            Seed(PetColorSeedConstants.GrayBlack, "GRAY_BLACK", "Gri-Siyah", 29),
            Seed(PetColorSeedConstants.BrownCream, "BROWN_CREAM", "Kahverengi-Krem", 30),
            Seed(PetColorSeedConstants.BeigeWhite, "BEIGE_WHITE", "Bej-Beyaz", 31),
            Seed(PetColorSeedConstants.BrownBrindle, "BROWN_BRINDLE", "Kahverengi-Kırçıllı", 32),
            Seed(PetColorSeedConstants.Champagne, "CHAMPAGNE", "Şampanya", 33),
            Seed(PetColorSeedConstants.Chocolate, "CHOCOLATE", "Çikolata", 34),
            Seed(PetColorSeedConstants.OrangeWhite, "ORANGE_WHITE", "Turuncu-Beyaz", 35),
            Seed(PetColorSeedConstants.Calico, "CALICO", "Üç Renkli (Calico)", 36),
            // Diğer ve Bilinmiyor listenin sonunda kalır.
            Seed(PetColorSeedConstants.Other, "OTHER", "Diğer", 98),
            Seed(PetColorSeedConstants.Unknown, "UNKNOWN", "Bilinmiyor", 99));
    }

    private static PetColor Seed(Guid id, string code, string name, int displayOrder)
    {
        var entity = new PetColor(code, name, displayOrder);
        typeof(PetColor).GetProperty(nameof(PetColor.Id))!.SetValue(entity, id);
        return entity;
    }
}
