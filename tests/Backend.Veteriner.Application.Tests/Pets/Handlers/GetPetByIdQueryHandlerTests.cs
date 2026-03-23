using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Queries.GetById;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class GetPetByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();

    private GetPetByIdQueryHandler CreateHandler() => new(_tenant.Object, _pets.Object);

    [Fact]
    public async Task Handle_Should_MapSpeciesName_When_SpeciesLoaded()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        var species = new Species("CAT", "Kedi");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);
        var pet = new Pet(tid, cid, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Species))!.SetValue(pet, species);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, pid);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var result = await CreateHandler().Handle(new GetPetByIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SpeciesId.Should().Be(TestSpeciesIds.Cat);
        result.Value.SpeciesName.Should().Be("Kedi");
    }
}
