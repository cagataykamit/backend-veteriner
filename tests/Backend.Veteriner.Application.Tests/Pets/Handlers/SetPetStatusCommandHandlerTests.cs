using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Commands.SetStatus;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.Handlers;

public sealed class SetPetStatusCommandHandlerTests
{
    private static readonly string ValidRowVersion = Convert.ToBase64String(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });

    // 2026-10-14 21:30 UTC = İstanbul 2026-10-15 00:30; "bugün" İstanbul günüdür.
    private static readonly DateTime NowUtc = new(2026, 10, 14, 21, 30, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IRepository<Pet>> _pets = new();

    public SetPetStatusCommandHandlerTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(_tenantId);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant("A"));
    }

    private SetPetStatusCommandHandler CreateHandler()
        => new(_tenantContext.Object, _tenants.Object, _pets.Object, new FixedClock(NowUtc));

    private Pet ReturnPet()
    {
        var pet = new Pet(_tenantId, Guid.NewGuid(), "Pamuk", Guid.NewGuid());
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        return pet;
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(Guid.NewGuid(), PetStatus.Deceased, null, ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_Pet_NotFound()
    {
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetForUpdateByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(Guid.NewGuid(), PetStatus.Deceased, null, ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.NotFound");
    }

    [Fact]
    public async Task Handle_Should_Fail_When_RowVersion_Missing()
    {
        var pet = ReturnPet();

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(pet.Id, PetStatus.Deceased, null, null), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.Validation");
        pet.Status.Should().Be(PetStatus.Active);
        _pets.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Mark_Deceased_Using_Istanbul_Today()
    {
        var pet = ReturnPet();
        var istanbulToday = new DateOnly(2026, 10, 15);

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(pet.Id, PetStatus.Deceased, istanbulToday, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(PetStatus.Deceased);
        result.Value.DeceasedOn.Should().Be(istanbulToday);
        _pets.Verify(r => r.UpdateAsync(pet, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Reject_Future_Date()
    {
        var pet = ReturnPet();

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(pet.Id, PetStatus.Deceased, new DateOnly(2026, 10, 16), ValidRowVersion), CancellationToken.None);

        result.Error.Code.Should().Be("Pets.Validation");
        _pets.Verify(r => r.UpdateAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Revert_To_Active()
    {
        var pet = ReturnPet();
        pet.SetStatus(PetStatus.Deceased, null, new DateOnly(2026, 10, 1));

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(pet.Id, PetStatus.Active, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(PetStatus.Active);
        result.Value.DeceasedOn.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_Map_Concurrency_To_Conflict()
    {
        var pet = ReturnPet();
        _pets.Setup(r => r.UpdateAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        var result = await CreateHandler().Handle(
            new SetPetStatusCommand(pet.Id, PetStatus.Deceased, null, ValidRowVersion), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.ConcurrencyConflict");
    }

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
