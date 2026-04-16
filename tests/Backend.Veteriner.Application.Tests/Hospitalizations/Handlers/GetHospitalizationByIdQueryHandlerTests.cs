using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Queries.GetById;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Handlers;

public sealed class GetHospitalizationByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetHospitalizationByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _hospitalizations.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _hospitalizations.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Hospitalization>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_HospitalizationNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_MaskAsNotFound_When_ActiveClinicContext_DoesNotMatch_RowClinic()
    {
        var tid = Guid.NewGuid();
        var contextClinicId = Guid.NewGuid();
        var rowClinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(contextClinicId);

        var h = new Hospitalization(
            tid,
            rowClinicId,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            null,
            "Neden",
            null);

        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(h);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(h.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_Found_And_ClinicContextAllows()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var petId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var h = new Hospitalization(
            tid,
            clinicId,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(2),
            "Sebep",
            "Not");

        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(h);

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(h.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.IsActive.Should().BeTrue();
        dto.Reason.Should().Be("Sebep");
        dto.Notes.Should().Be("Not");
        dto.TenantId.Should().Be(tid);
    }

    [Fact]
    public async Task Handle_Should_SetIsActiveFalse_When_Discharged()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var h = new Hospitalization(
            tid,
            clinicId,
            petId,
            null,
            DateTime.UtcNow.AddDays(-3),
            null,
            "Taburcu",
            null);
        typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!
            .SetValue(h, DateTime.UtcNow.AddDays(-1));

        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(h);

        var pet = new Pet(tid, clientId, "X", TestSpeciesIds.Dog, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Müşteri");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(h.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeFalse();
    }
}
