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
}
