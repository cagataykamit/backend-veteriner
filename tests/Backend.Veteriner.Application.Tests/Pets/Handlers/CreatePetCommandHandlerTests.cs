using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class CreatePetCommandHandlerTests
{
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IReadRepository<Pet>> _petsRead = new();
    private readonly Mock<IRepository<Pet>> _petsWrite = new();

    private CreatePetCommandHandler CreateHandler()
        => new(_tenantsRead.Object, _clientsRead.Object, _petsRead.Object, _petsWrite.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantNotFound()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var cmd = new CreatePetCommand(tid, cid, "Pamuk", "Kedi", null, null);

        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.NotFound");
        _petsWrite.Verify(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantInactive()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var cmd = new CreatePetCommand(tid, cid, "Pamuk", "Kedi", null, null);

        var tenant = new Tenant("X");
        tenant.Deactivate();

        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.TenantInactive");
        _petsWrite.Verify(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ClientNotFound()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var cmd = new CreatePetCommand(tid, cid, "Pamuk", "Kedi", null, null);

        var tenant = new Tenant("X");

        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Client?)null);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clients.NotFound");
        _petsWrite.Verify(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_BirthDateInFuture()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var cmd = new CreatePetCommand(tid, cid, "Pamuk", "Kedi", null, future);

        var tenant = new Tenant("X");
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", null));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.BirthDateInFuture");
        _petsWrite.Verify(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicatePet()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var cmd = new CreatePetCommand(tid, cid, "Pamuk", "Kedi", "Tekir", null);

        var tenant = new Tenant("X");
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", null));

        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, cid, "pamuk", "KEDİ", null, null));

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.DuplicatePet");
        _petsWrite.Verify(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreatePet_When_Valid()
    {
        var handler = CreateHandler();
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var birth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2));
        var cmd = new CreatePetCommand(tid, cid, "Pamuk", "Kedi", "Tekir", birth);

        var tenant = new Tenant("X");
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client(tid, "Ali", null));

        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        Pet? captured = null;
        _petsWrite.Setup(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()))
            .Callback<Pet, CancellationToken>((p, _) => captured = p);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Pamuk");
        captured.Species.Should().Be("Kedi");
        captured.Breed.Should().Be("Tekir");
        captured.BirthDate.Should().Be(birth);

        _petsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
