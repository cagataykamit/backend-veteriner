using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Application.Pets.Commands.Update;
using Backend.Veteriner.Application.Pets.IntegrationEvents;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Pets.IntegrationEvents;

public sealed class PetCommandHandlerOutboxEmissionTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IReadRepository<TenantSubscription>> _subscriptionsRead = new();
    private readonly Mock<IReadRepository<Client>> _clientsRead = new();
    private readonly Mock<IReadRepository<Species>> _speciesRead = new();
    private readonly Mock<IReadRepository<PetColor>> _colorsRead = new();
    private readonly Mock<IReadRepository<Breed>> _breedsRead = new();
    private readonly Mock<IReadRepository<Pet>> _petsRead = new();
    private readonly Mock<IRepository<Pet>> _petsWrite = new();
    private readonly Mock<IPetIntegrationEventOutbox> _eventOutbox = new();

    private CreatePetCommandHandler CreateCreateHandler()
    {
        _subscriptionsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantSubscriptionByTenantIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TenantSubscription.StartTrial(Guid.NewGuid(), SubscriptionPlanCode.Basic, DateTime.UtcNow, 14));

        var writeEvaluator = new TenantSubscriptionEffectiveWriteEvaluator(_tenantsRead.Object, _subscriptionsRead.Object);

        return new(
            _tenantContext.Object,
            _tenantsRead.Object,
            _clientsRead.Object,
            _speciesRead.Object,
            _colorsRead.Object,
            _breedsRead.Object,
            _petsRead.Object,
            _petsWrite.Object,
            writeEvaluator,
            _eventOutbox.Object);
    }

    private UpdatePetCommandHandler CreateUpdateHandler()
        => new(
            _tenantContext.Object,
            _tenantsRead.Object,
            _clientsRead.Object,
            _speciesRead.Object,
            _colorsRead.Object,
            _breedsRead.Object,
            _petsRead.Object,
            _petsWrite.Object,
            _eventOutbox.Object);

    private static Tenant ActiveTenant(Guid tid)
    {
        var tenant = new Tenant("Klinik A.Ş.");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        return tenant;
    }

    private void SetupActiveTenant(Guid tid)
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _tenantsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveTenant(tid));
    }

    private static Client CreateClient(Guid tid, string fullName = "Ayşe Yılmaz")
        => new(tid, fullName, "05321234567", "ayse@example.com");

    private static Species CreateSpecies(string name = "Kedi")
        => new("CAT", name);

    [Fact]
    public async Task Create_Should_Emit_PetCreated_With_CorrectPayload()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var breedId = Guid.NewGuid();
        var colorId = Guid.NewGuid();
        SetupActiveTenant(tid);

        var client = CreateClient(tid);
        var species = CreateSpecies("Kedi");
        var breed = new Breed(species.Id, "Tekir");
        typeof(Breed).GetProperty(nameof(Breed.Id))!.SetValue(breed, breedId);
        var color = new PetColor("BLACK", "Siyah");

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.SpeciesReference.Specs.SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(species);
        _breedsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.BreedsReference.Specs.BreedByIdWithSpeciesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(breed);
        _colorsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.PetColors.Specs.PetColorByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(color);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        Pet? captured = null;
        _petsWrite.Setup(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()))
            .Callback<Pet, CancellationToken>((p, _) => captured = p);

        PetCreatedIntegrationEvent? evt = null;
        _eventOutbox
            .Setup(o => o.EnqueueAsync(
                PetIntegrationEventTypes.Created,
                It.IsAny<PetCreatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PetCreatedIntegrationEvent, CancellationToken>((_, e, _) => evt = e)
            .Returns(Task.CompletedTask);

        var cmd = new CreatePetCommand(
            cid,
            "Pamuk",
            species.Id,
            "Serbest Irk",
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-2)),
            breedId,
            PetGender.Female,
            colorId,
            4.25m,
            "Not");

        var result = await CreateCreateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();

        evt.Should().NotBeNull();
        evt!.EventId.Should().NotBeEmpty();
        evt.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var snap = evt.Current;
        snap.PetId.Should().Be(captured!.Id);
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

        _eventOutbox.Verify(o => o.EnqueueAsync(
            PetIntegrationEventTypes.Created,
            It.IsAny<PetCreatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _petsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Should_NotEmit_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateCreateHandler().Handle(
            new CreatePetCommand(Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _petsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Should_NotEmit_When_DuplicatePet()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupActiveTenant(tid);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateClient(tid));
        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.SpeciesReference.Specs.SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSpecies());
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Pet(tid, cid, "pamuk", TestSpeciesIds.Cat, null, null));

        var result = await CreateCreateHandler().Handle(
            new CreatePetCommand(cid, "Pamuk", TestSpeciesIds.Cat, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.DuplicatePet");
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _petsWrite.Verify(r => r.AddAsync(It.IsAny<Pet>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Emit_PetUpdated_With_CorrectPayload()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var pid = Guid.NewGuid();
        SetupActiveTenant(tid);

        var client = CreateClient(tid, "Mehmet Demir");
        var species = CreateSpecies("Köpek");
        var existing = new Pet(tid, cid, "Eski", species.Id, "Eski Irk", null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(existing, pid);

        _clientsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.Clients.Specs.ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
        _speciesRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Backend.Veteriner.Application.SpeciesReference.Specs.SpeciesByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(species);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByClientNameAndSpeciesIdExcludingIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        PetUpdatedIntegrationEvent? evt = null;
        _eventOutbox
            .Setup(o => o.EnqueueAsync(
                PetIntegrationEventTypes.Updated,
                It.IsAny<PetUpdatedIntegrationEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PetUpdatedIntegrationEvent, CancellationToken>((_, e, _) => evt = e)
            .Returns(Task.CompletedTask);

        var cmd = new UpdatePetCommand(pid, cid, "Pamuk", species.Id, "Golden", null, null, PetGender.Male, null, 12.5m, null);
        var result = await CreateUpdateHandler().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        evt.Should().NotBeNull();
        evt!.EventId.Should().NotBeEmpty();
        evt.OccurredAtUtc.Kind.Should().Be(DateTimeKind.Utc);

        var snap = evt.Current;
        snap.PetId.Should().Be(pid);
        snap.TenantId.Should().Be(tid);
        snap.ClientId.Should().Be(cid);
        snap.Name.Should().Be("Pamuk");
        snap.NameNormalized.Should().Be("pamuk");
        snap.SpeciesName.Should().Be("Köpek");
        snap.ClientFullName.Should().Be("Mehmet Demir");
        snap.Breed.Should().Be("Golden");
        snap.Gender.Should().Be((int)PetGender.Male);
        snap.Weight.Should().Be(12.5m);

        _eventOutbox.Verify(o => o.EnqueueAsync(
            PetIntegrationEventTypes.Updated,
            It.IsAny<PetUpdatedIntegrationEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _petsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_NotEmit_When_PetNotFound()
    {
        var tid = Guid.NewGuid();
        SetupActiveTenant(tid);
        _petsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Pet?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdatePetCommand(Guid.NewGuid(), Guid.NewGuid(), "Pamuk", TestSpeciesIds.Cat, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Pets.NotFound");
        _eventOutbox.Verify(o => o.EnqueueAsync(
            It.IsAny<string>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _petsWrite.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
