using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Application.Treatments.Queries.GetById;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Treatments.Handlers;

public sealed class GetTreatmentByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetTreatmentByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _treatments.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _treatments.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Treatment>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_TreatmentNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment?)null);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
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

        var tr = new Treatment(
            tid,
            rowClinicId,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            "T",
            "D",
            null,
            null);

        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tr);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(tr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
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
        var examId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var tr = new Treatment(
            tid,
            clinicId,
            petId,
            examId,
            DateTime.UtcNow.AddDays(-1),
            "Başlık",
            "Uzun açıklama",
            "Notlar",
            DateTime.UtcNow.AddDays(5));

        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tr);

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(tr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Id.Should().Be(tr.Id);
        dto.TenantId.Should().Be(tid);
        dto.ClinicId.Should().Be(clinicId);
        dto.PetId.Should().Be(petId);
        dto.PetName.Should().Be("Pamuk");
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.ExaminationId.Should().Be(examId);
        dto.Title.Should().Be("Başlık");
        dto.Description.Should().Be("Uzun açıklama");
        dto.Notes.Should().Be("Notlar");
        dto.TreatmentDateUtc.Should().Be(tr.TreatmentDateUtc);
        dto.FollowUpDateUtc.Should().Be(tr.FollowUpDateUtc);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_NoClinicContext_EvenIf_RowInOtherClinic()
    {
        var tid = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var tr = new Treatment(
            tid,
            clinicId,
            petId,
            null,
            DateTime.UtcNow,
            "T",
            "D",
            null,
            null);

        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tr);

        var pet = new Pet(tid, clientId, "X", TestSpeciesIds.Dog, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Müşteri");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(tr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
    }
}
