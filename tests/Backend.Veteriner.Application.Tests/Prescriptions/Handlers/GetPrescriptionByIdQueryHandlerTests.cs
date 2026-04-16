using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Prescriptions.Queries.GetById;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Prescriptions.Handlers;

public sealed class GetPrescriptionByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptions = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetPrescriptionByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _prescriptions.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _prescriptions.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<Prescription>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_PrescriptionNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _prescriptions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.NotFound");
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

        var pr = new Prescription(
            tid,
            rowClinicId,
            petId,
            null,
            null,
            DateTime.UtcNow.AddDays(-1),
            "T",
            "C",
            null,
            null);

        _prescriptions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(pr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.NotFound");
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
        var treatmentId = Guid.NewGuid();

        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var pr = new Prescription(
            tid,
            clinicId,
            petId,
            examId,
            treatmentId,
            DateTime.UtcNow.AddDays(-1),
            "Başlık",
            "İçerik gövdesi",
            "Notlar",
            DateTime.UtcNow.AddDays(3));

        _prescriptions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(pr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Id.Should().Be(pr.Id);
        dto.TenantId.Should().Be(tid);
        dto.ClinicId.Should().Be(clinicId);
        dto.PetId.Should().Be(petId);
        dto.PetName.Should().Be("Pamuk");
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.ExaminationId.Should().Be(examId);
        dto.TreatmentId.Should().Be(treatmentId);
        dto.Title.Should().Be("Başlık");
        dto.Content.Should().Be("İçerik gövdesi");
        dto.Notes.Should().Be("Notlar");
        dto.PrescribedAtUtc.Should().Be(pr.PrescribedAtUtc);
        dto.FollowUpDateUtc.Should().Be(pr.FollowUpDateUtc);
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

        var pr = new Prescription(
            tid,
            clinicId,
            petId,
            null,
            null,
            DateTime.UtcNow,
            "T",
            "C",
            null,
            null);

        _prescriptions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pr);

        var pet = new Pet(tid, clientId, "X", TestSpeciesIds.Dog, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Müşteri");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(pr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
    }
}
