using Backend.Veteriner.Domain.Pets;
using AwesomeAssertions;

namespace Backend.Veteriner.Domain.Tests.Pets;

public sealed class PetCoatTests
{
    private static Pet Cat(Guid? colorId = null, Guid? patternId = null)
        => new(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", Guid.NewGuid(), "Tekir", colorId: colorId, patternId: patternId);

    [Fact]
    public void Pattern_Should_Be_Stored_Separately_From_Color()
    {
        var color = Guid.NewGuid();
        var pattern = Guid.NewGuid();

        var pet = Cat(color, pattern);

        pet.ColorId.Should().Be(color);
        pet.PatternId.Should().Be(pattern);
    }

    [Fact]
    public void ReclassifyCoatFromBreed_Should_Move_Breed_And_Fill_Only_Empty_Coat_Fields()
    {
        var breedId = Guid.NewGuid();
        var vetColor = Guid.NewGuid();
        var pet = Cat(colorId: vetColor);

        pet.ReclassifyCoatFromBreed(breedId, "Ev Kedisi (Kısa Tüylü)", colorId: Guid.NewGuid(), patternId: Guid.NewGuid());

        pet.BreedId.Should().Be(breedId);
        pet.Breed.Should().Be("Ev Kedisi (Kısa Tüylü)");
        // Hekimin girdiği renk korunur; boş desen doldurulur.
        pet.ColorId.Should().Be(vetColor);
        pet.PatternId.Should().NotBeNull();
    }
}
