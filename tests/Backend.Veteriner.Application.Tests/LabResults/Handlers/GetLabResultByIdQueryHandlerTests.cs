using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LabResults.Queries.GetById;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tests;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Pets;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.LabResults.Handlers;

public sealed class GetLabResultByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IReadRepository<LabResult>> _labResults = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private GetLabResultByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _labResults.Object,
            _pets.Object,
            _clients.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_TenantContextMissing()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns((Guid?)null);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _labResults.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<ISpecification<LabResult>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_LabResultNotFound()
    {
        var tid = Guid.NewGuid();
        _tenantContext.SetupGet(t => t.TenantId).Returns(tid);
        _labResults.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult?)null);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
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

        var lr = new LabResult(
            tid,
            rowClinicId,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            "T",
            "R",
            null,
            null);

        _labResults.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lr);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(lr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
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

        var lr = new LabResult(
            tid,
            clinicId,
            petId,
            examId,
            DateTime.UtcNow.AddDays(-1),
            "Test adı",
            "Sonuç metni",
            "Yorum",
            "Not");

        _labResults.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lr);

        var pet = new Pet(tid, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(lr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Id.Should().Be(lr.Id);
        dto.TenantId.Should().Be(tid);
        dto.ClinicId.Should().Be(clinicId);
        dto.PetId.Should().Be(petId);
        dto.PetName.Should().Be("Pamuk");
        dto.ClientId.Should().Be(clientId);
        dto.ClientName.Should().Be("Ali Veli");
        dto.ExaminationId.Should().Be(examId);
        dto.TestName.Should().Be("Test adı");
        dto.ResultText.Should().Be("Sonuç metni");
        dto.Interpretation.Should().Be("Yorum");
        dto.Notes.Should().Be("Not");
        dto.ResultDateUtc.Should().Be(lr.ResultDateUtc);
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

        var lr = new LabResult(
            tid,
            clinicId,
            petId,
            null,
            DateTime.UtcNow,
            "T",
            "R",
            null,
            null);

        _labResults.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lr);

        var pet = new Pet(tid, clientId, "X", TestSpeciesIds.Dog, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);
        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);

        var client = new Client(tid, "Müşteri");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(lr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
    }
}
