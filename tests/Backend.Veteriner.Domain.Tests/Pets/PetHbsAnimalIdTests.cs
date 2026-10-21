using Backend.Veteriner.Domain.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Domain.Tests.Pets;

/// <summary>ADR-036 Dilim 1: HBS hayvan kimlik no (null = dokunma, boş = temizle).</summary>
public sealed class PetHbsAnimalIdTests
{
    private static Pet NewPet() => new(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", Guid.NewGuid());

    [Fact]
    public void Apply_Should_Trim_And_Store()
    {
        var pet = NewPet();

        pet.ApplyHbsAnimalId("  TR-123  ").IsSuccess.Should().BeTrue();

        pet.HbsAnimalId.Should().Be("TR-123");
    }

    [Fact]
    public void Null_Should_Keep_And_Empty_Should_Clear()
    {
        var pet = NewPet();
        pet.ApplyHbsAnimalId("TR-123");

        pet.ApplyHbsAnimalId(null).IsSuccess.Should().BeTrue();
        pet.HbsAnimalId.Should().Be("TR-123");

        pet.ApplyHbsAnimalId("   ").IsSuccess.Should().BeTrue();
        pet.HbsAnimalId.Should().BeNull();
    }

    [Fact]
    public void Too_Long_Should_Be_Rejected()
    {
        var pet = NewPet();

        var result = pet.ApplyHbsAnimalId(new string('1', Pet.MaxHbsAnimalIdLength + 1));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.Validation");
        pet.HbsAnimalId.Should().BeNull();
    }
}
