using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Commands.Update;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.SpeciesReference.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class UpdatePetCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IReadRepository<Species>> _speciesRead = new();
    private readonly Mock<IReadRepository<PetColor>> _colorsRead = new();
    private readonly Mock<IReadRepository<Breed>> _breedsRead = new();
    private readonly Mock<IReadRepository<Pet>> _petsRead = new();
    private readonly Mock<IRepository<Pet>> _petsWrite = new();

    private UpdatePetCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _tenantsRead.Object,
            _clientsRead.Object,
            _speciesRead.Object,
            _colorsRead.Object,
            _breedsRead.Object,
            _petsRead.Object,
            _petsWrite.Object);

    private static void AlignTenantId(Tenant tenant, Guid id)
        => typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, id);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PetMissing()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cmd = new UpdatePetCommand(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, null, null);

        var tenant = new Tenant("X");
        AlignTenantId(tenant, tid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateNameUnderSameClientSpecies()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cmd = new UpdatePetCommand(pid, cid, "Pamuk", TestSpeciesIds.Cat, null, null);

        var tenant = new Tenant("X");
        AlignTenantId(tenant, tid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, cid, "Old", TestSpeciesIds.Cat, null, null));
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", null));
        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("CAT", "Kedi"));
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesIdExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, cid, "pamuk", TestSpeciesIds.Cat, null, null));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.DuplicatePet");
    }

    [Fact]
    public async Task Handle_Should_UpdatePet_When_Valid()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cmd = new UpdatePetCommand(
            pid,
            cid,
            "Pamuk",
            TestSpeciesIds.Dog,
            "Golden",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1)),
            null,
            PetGender.Male,
            null,
            12.5m,
            "Kontrol sonrası kilo takip edilecek.");

        var tenant = new Tenant("X");
        AlignTenantId(tenant, tid);
        var existing = new Pet(tid, cid, "Old", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(existing, pid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", null));
        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("DOG", "Kopek"));
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesIdExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        existing.Name.Should().Be("Pamuk");
        existing.SpeciesId.Should().Be(TestSpeciesIds.Dog);
        existing.Breed.Should().Be("Golden");
        existing.Gender.Should().Be(PetGender.Male);
        existing.Weight.Should().Be(12.5m);
        existing.Notes.Should().Be("Kontrol sonrası kilo takip edilecek.");
        _petsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ColorNotFound()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        var cmd = new UpdatePetCommand(pid, cid, "Pamuk", TestSpeciesIds.Cat, null, null, null, null, Guid.NewGuid());

        var tenant = new Tenant("X");
        AlignTenantId(tenant, tid);
        var existing = new Pet(tid, cid, "Old", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(existing, pid);

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", null));
        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Species("CAT", "Kedi"));
        _colorsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.PetColors.Specs.PetColorByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PetColor?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.ColorNotFound");
    }
}