using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Queries.GetById;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class GetPetByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetPetByIdQueryHandler CreateHandler() => new(_tenant.Object, _pets.Object, _clients.Object);

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
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(new GetPetByIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SpeciesId.Should().Be(TestSpeciesIds.Cat);
        result.Value.SpeciesName.Should().Be("Kedi");
        result.Value.BreedId.Should().BeNull();
        result.Value.Gender.Should().BeNull();
        result.Value.ClientName.Should().BeEmpty();
        result.Value.ClientPhone.Should().BeNull();
        result.Value.ClientEmail.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_MapBreedIdAndGender_When_Set()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var breedId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        var species = new Species("CAT", "Kedi");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);
        var pet = new Pet(tid, cid, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, pid);
        typeof(Pet).GetProperty(nameof(Pet.Species))!.SetValue(pet, species);
        typeof(Pet).GetProperty(nameof(Pet.BreedId))!.SetValue(pet, breedId);
        typeof(Pet).GetProperty(nameof(Pet.Gender))!.SetValue(pet, PetGender.Female);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(new GetPetByIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.BreedId.Should().Be(breedId);
        result.Value.Gender.Should().Be(PetGender.Female);
    }

    [Fact]
    public async Task Handle_Should_MapColorIdAndColorName_When_Set()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var colorId = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        var species = new Species("CAT", "Kedi");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);
        var color = new PetColor("BLACK", "Siyah", 1);
        typeof(PetColor).GetProperty(nameof(PetColor.Id))!.SetValue(color, colorId);
        var pet = new Pet(tid, cid, "Pamuk", TestSpeciesIds.Cat, null, null, null, null, colorId);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, pid);
        typeof(Pet).GetProperty(nameof(Pet.Species))!.SetValue(pet, species);
        typeof(Pet).GetProperty(nameof(Pet.ColorRef))!.SetValue(pet, color);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await CreateHandler().Handle(new GetPetByIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ColorId.Should().Be(colorId);
        result.Value.ColorName.Should().Be("Siyah");
    }

    [Fact]
    public async Task Handle_Should_MapClientSummary_When_ClientFound()
    {
        var tid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _tenant.SetupGet(t => t.TenantId).Returns(tid);

        var species = new Species("CAT", "Kedi");
        typeof(Species).GetProperty(nameof(Species.Id))!.SetValue(species, TestSpeciesIds.Cat);
        var pet = new Pet(tid, cid, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, pid);
        typeof(Pet).GetProperty(nameof(Pet.Species))!.SetValue(pet, species);

        var owner = new Client(tid, "Ali Veli", "05321234567", "ali@example.com");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(owner, cid);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(owner);

        var result = await CreateHandler().Handle(new GetPetByIdQuery(pid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClientName.Should().Be("Ali Veli");
        result.Value.ClientPhone.Should().NotBeNullOrEmpty();
        result.Value.ClientEmail.Should().Be("ali@example.com");
    }
}
