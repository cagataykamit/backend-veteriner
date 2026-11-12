using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Backend.Veteriner.Infrastructure.Persistence.Configurations;

/// <summary>
/// Kürk rengi ve deseni kataloğu. Renk ile desen FIFe EMS'de olduğu gibi ayrıdır (<see cref="PetColorKind"/>);
/// kaynaklar: vetinity-product research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §5.
/// </summary>
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

        b.Property(x => x.Kind)
            .IsRequired();

        b.HasIndex(x => x.Code)
            .IsUnique();

        b.HasData(
            Color(PetColorSeedConstants.Black, "BLACK", "Siyah", 1),
            Color(PetColorSeedConstants.White, "WHITE", "Beyaz", 2),
            Color(PetColorSeedConstants.Brown, "BROWN", "Kahverengi", 3),
            Color(PetColorSeedConstants.Gray, "GRAY", "Gri", 4),
            Color(PetColorSeedConstants.Yellow, "YELLOW", "Sarı", 5),
            Color(PetColorSeedConstants.Cream, "CREAM", "Krem", 6),
            Color(PetColorSeedConstants.Red, "RED", "Kızıl", 7),
            Color(PetColorSeedConstants.Orange, "ORANGE", "Turuncu", 8),
            Color(PetColorSeedConstants.BlackWhite, "BLACK_WHITE", "Siyah-Beyaz", 9),
            Color(PetColorSeedConstants.BrownWhite, "BROWN_WHITE", "Kahverengi-Beyaz", 10),
            Color(PetColorSeedConstants.GrayWhite, "GRAY_WHITE", "Gri-Beyaz", 11),
            Color(PetColorSeedConstants.MultiColor, "MULTI_COLOR", "Çok Renkli", 15),
            Color(PetColorSeedConstants.YellowWhite, "YELLOW_WHITE", "Sarı-Beyaz", 22),
            Color(PetColorSeedConstants.BrownBlack, "BROWN_BLACK", "Kahverengi-Siyah", 23),
            Color(PetColorSeedConstants.LightBrown, "LIGHT_BROWN", "Açık Kahverengi", 24),
            Color(PetColorSeedConstants.Dun, "DUN", "Boz", 25),
            Color(PetColorSeedConstants.Beige, "BEIGE", "Bej", 26),
            Color(PetColorSeedConstants.Blue, "BLUE", "Mavi (Seyreltik Siyah)", 27),
            Color(PetColorSeedConstants.BlackYellow, "BLACK_YELLOW", "Siyah-Sarı", 28),
            Color(PetColorSeedConstants.GrayBlack, "GRAY_BLACK", "Gri-Siyah", 29),
            Color(PetColorSeedConstants.BrownCream, "BROWN_CREAM", "Kahverengi-Krem", 30),
            Color(PetColorSeedConstants.BeigeWhite, "BEIGE_WHITE", "Bej-Beyaz", 31),
            Color(PetColorSeedConstants.Champagne, "CHAMPAGNE", "Şampanya", 33),
            Color(PetColorSeedConstants.Chocolate, "CHOCOLATE", "Çikolata", 34),
            Color(PetColorSeedConstants.OrangeWhite, "ORANGE_WHITE", "Turuncu-Beyaz", 35),
            Color(PetColorSeedConstants.Smoke, "SMOKE", "Dumanlı (Smoke)", 37),
            Color(PetColorSeedConstants.Silver, "SILVER", "Gümüş (Silver)", 38),
            Color(PetColorSeedConstants.Lilac, "LILAC", "Lila (Lilac)", 39),
            Color(PetColorSeedConstants.Cinnamon, "CINNAMON", "Tarçın (Cinnamon)", 40),
            Color(PetColorSeedConstants.Fawn, "FAWN", "Fawn (Açık Sarı-Kahve)", 41),
            Color(PetColorSeedConstants.BlackTan, "BLACK_TAN", "Siyah-Bakır (Black and Tan)", 42),
            Color(PetColorSeedConstants.Sable, "SABLE", "Samur (Sable)", 43),
            Color(PetColorSeedConstants.Tricolor, "TRICOLOR", "Siyah-Beyaz-Kahve (Tricolor)", 44),
            // At donları
            Color(PetColorSeedConstants.Bay, "HORSE_BAY", "Doru (At)", 60),
            Color(PetColorSeedConstants.Chestnut, "HORSE_CHESTNUT", "Al (At)", 61),
            Color(PetColorSeedConstants.HorseGrey, "HORSE_GREY", "Kır (At)", 62),
            Color(PetColorSeedConstants.HorseBlack, "HORSE_BLACK", "Yağız (At)", 63),
            Color(PetColorSeedConstants.HorseDun, "HORSE_DUN", "Kula (At)", 64),
            Color(PetColorSeedConstants.Isabelline, "HORSE_ISABELLINE", "İzabel (At)", 65),
            // Diğer ve Bilinmiyor renk listesinin sonunda kalır.
            Color(PetColorSeedConstants.Other, "OTHER", "Diğer", 98),
            Color(PetColorSeedConstants.Unknown, "UNKNOWN", "Bilinmiyor", 99),
            // Desenler
            Pattern(PetColorSeedConstants.Tabby, "TABBY", "Tekir (Tabby)", 101),
            Pattern(PetColorSeedConstants.Brindle, "BRINDLE", "Kaplan Çizgili (Brindle)", 102),
            Pattern(PetColorSeedConstants.Spotted, "SPOTTED", "Benekli", 103),
            Pattern(PetColorSeedConstants.Striped, "STRIPED", "Çizgili", 104),
            Pattern(PetColorSeedConstants.Piebald, "PIEBALD", "Alacalı", 105),
            Pattern(PetColorSeedConstants.Calico, "CALICO", "Üç Renkli (Calico)", 106),
            Pattern(PetColorSeedConstants.Tortoiseshell, "TORTOISESHELL", "Kaplumbağa Kabuğu (Tortoiseshell)", 107),
            Pattern(PetColorSeedConstants.Colorpoint, "COLORPOINT", "Sivri Renk (Colorpoint)", 108),
            Pattern(PetColorSeedConstants.VanPattern, "VAN", "Van Deseni", 109),
            Pattern(PetColorSeedConstants.Merle, "MERLE", "Merle", 110),
            Pattern(PetColorSeedConstants.Harlequin, "HARLEQUIN", "Harlequin", 111),
            Pattern(PetColorSeedConstants.Roan, "ROAN", "Kırçıl (Roan)", 112),
            // Renk + desen birleşimi olan eski satırlar: hastalar renk ve desene ayrıldı (AddPetCoatPattern), seçimde görünmez.
            Retired(PetColorSeedConstants.BrindleWhite, "BRINDLE_WHITE", "Kaplan Çizgili-Beyaz", 21),
            Retired(PetColorSeedConstants.BrownBrindle, "BROWN_BRINDLE", "Kahverengi Kaplan Çizgili", 32));
    }

    private static PetColor Color(Guid id, string code, string name, int displayOrder)
        => Seed(id, code, name, displayOrder, PetColorKind.Color, isActive: true);

    private static PetColor Pattern(Guid id, string code, string name, int displayOrder)
        => Seed(id, code, name, displayOrder, PetColorKind.Pattern, isActive: true);

    private static PetColor Retired(Guid id, string code, string name, int displayOrder)
        => Seed(id, code, name, displayOrder, PetColorKind.Color, isActive: false);

    private static PetColor Seed(Guid id, string code, string name, int displayOrder, PetColorKind kind, bool isActive)
    {
        var entity = new PetColor(code, name, displayOrder, kind);
        if (!isActive)
            entity.Update(code, name, displayOrder, isActive: false);
        typeof(PetColor).GetProperty(nameof(PetColor.Id))!.SetValue(entity, id);
        return entity;
    }
}
