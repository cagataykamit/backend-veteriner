namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// PetColor seed kimlikleri; sabit GUID migration/seed senkronizasyonu için kullanılır.
/// </summary>
public static class PetColorSeedConstants
{
    public static readonly Guid Black = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020001");
    public static readonly Guid White = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020002");
    public static readonly Guid Brown = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020003");
    public static readonly Guid Gray = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020004");
    public static readonly Guid Yellow = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020005");
    public static readonly Guid Cream = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020006");
    public static readonly Guid Red = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020007");
    public static readonly Guid Orange = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020008");
    public static readonly Guid BlackWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020009");
    public static readonly Guid BrownWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020010");
    public static readonly Guid GrayWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020011");
    public static readonly Guid Spotted = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020012");
    public static readonly Guid Striped = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020013");
    // Alaca: birkaç renkli, yamalı tüy (kod 2026-11-12 öncesi CALICO idi).
    public static readonly Guid Piebald = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020014");
    public static readonly Guid MultiColor = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020015");
    public static readonly Guid Other = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020016");
    public static readonly Guid Unknown = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020017");

    // Türkiye'de yaygın tüy renkleri (E-vet geçişi 2026-11-12).
    public static readonly Guid Brindle = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020018");
    public static readonly Guid BrindleWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020019");
    public static readonly Guid YellowWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020020");
    public static readonly Guid BrownBlack = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020021");
    public static readonly Guid LightBrown = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020022");
    public static readonly Guid Dun = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020023");
    public static readonly Guid Beige = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020024");
    public static readonly Guid Blue = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020025");
    public static readonly Guid BlackYellow = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020026");
    public static readonly Guid GrayBlack = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020027");
    public static readonly Guid BrownCream = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020028");
    public static readonly Guid BeigeWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020029");
    public static readonly Guid BrownBrindle = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020030");
    public static readonly Guid Champagne = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020031");
    public static readonly Guid Chocolate = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020032");
    public static readonly Guid OrangeWhite = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020033");

    // Üç renkli kedi (siyah-turuncu-beyaz); alacadan ayrı desen.
    public static readonly Guid Calico = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020034");

    // Renk ile desen ayrımı (FIFe EMS; research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md §5, 2026-11-13).
    public static readonly Guid Smoke = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020035");
    public static readonly Guid Silver = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020036");
    public static readonly Guid Lilac = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020037");
    public static readonly Guid Cinnamon = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020038");
    public static readonly Guid Fawn = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020039");
    public static readonly Guid BlackTan = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020040");
    public static readonly Guid Sable = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020041");
    public static readonly Guid Tricolor = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020042");

    // At donları.
    public static readonly Guid Bay = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020043");
    public static readonly Guid Chestnut = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020044");
    public static readonly Guid HorseGrey = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020045");
    public static readonly Guid HorseBlack = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020046");
    public static readonly Guid HorseDun = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020047");
    public static readonly Guid Isabelline = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020048");

    // Desenler (Spotted, Striped, Piebald, Brindle ve Calico da desendir).
    public static readonly Guid Tabby = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020049");
    public static readonly Guid Tortoiseshell = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020050");
    public static readonly Guid Colorpoint = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020051");
    public static readonly Guid VanPattern = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020052");
    public static readonly Guid Merle = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020053");
    public static readonly Guid Harlequin = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020054");
    public static readonly Guid Roan = Guid.Parse("3d2d8f3a-6b0b-4d1e-9f2a-111111020055");
}
