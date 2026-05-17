namespace Backend.Veteriner.Infrastructure.Persistence.Seeding;

/// <summary>
/// İlk kurulum ve migration backfill ile aynı kimlikler; sabit GUID’ler veri taşımayı güvenilir kılar.
/// </summary>
public static class SpeciesSeedConstants
{
    public static readonly Guid Cat = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010001");
    public static readonly Guid Dog = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010002");
    public static readonly Guid Bird = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010003");
    public static readonly Guid Rabbit = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010004");
    public static readonly Guid Hamster = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010005");
    public static readonly Guid Turtle = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010006");
    public static readonly Guid Fish = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010007");
    public static readonly Guid Reptile = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010008");
    public static readonly Guid Other = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010099");

    /// <summary>Katalog seed ile eklenen türler (migration’daki sabit GUID’lerle çakışmaz).</summary>
    public static readonly Guid GuineaPig = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010200");

    public static readonly Guid Ferret = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010201");
    public static readonly Guid Chinchilla = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010202");
    public static readonly Guid Hedgehog = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010203");
    public static readonly Guid Lizard = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010204");
    public static readonly Guid Snake = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010205");
    public static readonly Guid Horse = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010206");
    public static readonly Guid Donkey = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010207");
    public static readonly Guid Mule = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010208");
    public static readonly Guid Cattle = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010209");
    public static readonly Guid Sheep = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010210");
    public static readonly Guid Goat = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010211");
    public static readonly Guid Pig = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010212");
    public static readonly Guid Chicken = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010213");
    public static readonly Guid Duck = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010214");
    public static readonly Guid Goose = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010215");
    public static readonly Guid Turkey = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010216");
}
