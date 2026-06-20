using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Pets.IntegrationEvents;

public sealed class PetProjectionSnapshotFactoryTests
{
    [Fact]
    public void Create_Should_MapAllFields_FromAggregateAndRelatedEntities()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var breedId = Guid.NewGuid();
        var colorId = Guid.NewGuid();

        var client = new Client(tid, "  Ayşe Yılmaz  ", "05321234567", "ayse@example.com");
        var species = new Species("CAT", "Kedi");
        var breed = new Breed(species.Id, "Tekir");
        typeof(Breed).GetProperty(nameof(Breed.Id))!.SetValue(breed, breedId);
        var color = new PetColor("BLACK", "Siyah");
        typeof(PetColor).GetProperty(nameof(PetColor.Id))!.SetValue(color, colorId);

        var pet = new Pet(
            tid,
            cid,
            "Pamuk",
            species.Id,
            "Serbest Irk",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
            breedId,
            PetGender.Female,
            colorId,
            4.25m);

        var snap = PetProjectionSnapshotFactory.Create(pet, client, species, breed, color);

        snap.PetId.Should().Be(pet.Id);
        snap.TenantId.Should().Be(tid);
        snap.ClientId.Should().Be(cid);
        snap.ClientFullName.Should().Be("Ayşe Yılmaz");
        snap.ClientFullNameNormalized.Should().Be(Client.NormalizeFullNameForDuplicateCheck("Ayşe Yılmaz"));
        snap.Name.Should().Be("Pamuk");
        snap.NameNormalized.Should().Be("pamuk");
        snap.SpeciesId.Should().Be(species.Id);
        snap.SpeciesName.Should().Be("Kedi");
        snap.SpeciesNameNormalized.Should().Be("kedi");
        snap.BreedId.Should().Be(breedId);
        snap.Breed.Should().Be("Serbest Irk");
        snap.BreedRefName.Should().Be("Tekir");
        snap.ColorId.Should().Be(colorId);
        snap.ColorName.Should().Be("Siyah");
        snap.ColorNameNormalized.Should().Be("siyah");
        snap.Gender.Should().Be((int)PetGender.Female);
        snap.Weight.Should().Be(4.25m);
    }

    [Fact]
    public void Create_Should_AllowNullOptionalFields()
    {
        var tid = Guid.NewGuid();
        var client = new Client(tid, "Ali Veli");
        var species = new Species("DOG", "Köpek");
        var pet = new Pet(tid, client.Id, "Rex", species.Id);

        var snap = PetProjectionSnapshotFactory.Create(pet, client, species);

        snap.BreedId.Should().BeNull();
        snap.Breed.Should().BeNull();
        snap.BreedRefName.Should().BeNull();
        snap.ColorId.Should().BeNull();
        snap.ColorName.Should().BeNull();
        snap.ColorNameNormalized.Should().BeNull();
        snap.Gender.Should().BeNull();
        snap.BirthDate.Should().BeNull();
        snap.Weight.Should().BeNull();
    }

    [Fact]
    public void Create_Should_BeDeterministic_ForSameInputs()
    {
        var tid = Guid.NewGuid();
        var client = new Client(tid, "Ali Veli");
        var species = new Species("CAT", "Kedi");
        var pet = new Pet(tid, client.Id, "Pamuk", species.Id);

        var first = PetProjectionSnapshotFactory.Create(pet, client, species);
        var second = PetProjectionSnapshotFactory.Create(pet, client, species);

        second.Should().Be(first);
    }
}
