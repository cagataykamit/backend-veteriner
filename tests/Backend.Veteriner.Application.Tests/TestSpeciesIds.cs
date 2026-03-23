namespace Backend.Veteriner.Application.Tests;

/// <summary>Migration seed ile aynı GUID’ler (Species tablosu CAT satırı).</summary>
public static class TestSpeciesIds
{
    public static readonly Guid Cat = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010001");
    public static readonly Guid Dog = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010002");
    public static readonly Guid Other = Guid.Parse("2c1c8f3a-6b0b-4d1e-9f2a-111111010099");
}
