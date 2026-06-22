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
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<LabResult>> _labResults = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetLabResultByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetLabResultByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _labResults.Object,
            _pets.Object,
            _clients.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static LabResult BuildLabResult(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? labResultId = null,
        Guid? examinationId = null)
    {
        var lr = new LabResult(
            tenantId,
            clinicId,
            petId,
            examinationId,
            DateTime.UtcNow.AddDays(-1),
            "Test adı",
            "Sonuç metni",
            "Yorum",
            "Not");
        if (labResultId.HasValue)
            typeof(LabResult).GetProperty(nameof(LabResult.Id))!.SetValue(lr, labResultId.Value);
        return lr;
    }

    private void SetupLabResultFound(LabResult labResult)
        => _labResults.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(labResult);

    private void SetupTenantWideRole(params string[] claimNames)
        => _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(claimNames);

    private void SetupAssignment(Guid clinicId, bool assigned)
        => _userClinics.Setup(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

    private void SetupDetailGraph(Guid tenantId, Guid petId, Guid clientId)
    {
        var pet = new Pet(tenantId, clientId, "Pamuk", TestSpeciesIds.Cat, null, null);
        typeof(Pet).GetProperty(nameof(Pet.Id))!.SetValue(pet, petId);

        var client = new Client(tenantId, "Ali Veli");
        typeof(Client).GetProperty(nameof(Client.Id))!.SetValue(client, clientId);

        _pets.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pet);
        _clients.Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClientByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(client);
    }

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
        SetupTenant(tid);
        _labResults.Setup(r => r.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult?)null);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_LabResultBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _labResults.Setup(x => x.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LabResult?)null);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideAdminReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var labResultId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupLabResultFound(BuildLabResult(tenantId, clinicId, petId, labResultId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(labResultId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(labResultId);
        _userClinics.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide Admin için UserClinic kontrolü çalıştırılmamalı");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideOwnerReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var labResultId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Owner");
        SetupLabResultFound(BuildLabResult(tenantId, clinicId, petId, labResultId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(labResultId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_PlatformAdminReadsWithinActiveTenant()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var examId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupTenantWideRole("PlatformAdmin");
        var entity = BuildLabResult(tenantId, clinicId, petId, examinationId: examId);
        SetupLabResultFound(entity);
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.ExaminationId.Should().Be(examId);
        result.Value.TestName.Should().Be("Test adı");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.ClientName.Should().Be("Ali Veli");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicLabResult()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var labResultId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupLabResultFound(BuildLabResult(tenantId, clinicId, petId, labResultId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(labResultId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToLabResultClinic_WithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var labResultId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupAssignment(clinicId, assigned: false);
        SetupLabResultFound(BuildLabResult(tenantId, clinicId, petId, labResultId));

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(labResultId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("LabResults.NotFound");
        _pets.Verify(
            r => r.FirstOrDefaultAsync(It.IsAny<PetByIdSpec>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_MaskAsNotFound_When_ActiveClinicContext_DoesNotMatch_RowClinic()
    {
        var tid = Guid.NewGuid();
        var contextClinicId = Guid.NewGuid();
        var rowClinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();

        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(contextClinicId);

        var lr = BuildLabResult(tid, rowClinicId, petId);
        SetupLabResultFound(lr);

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

        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        SetupTenantWideRole("Admin");

        var lr = BuildLabResult(tid, clinicId, petId, examinationId: examId);
        SetupLabResultFound(lr);
        SetupDetailGraph(tid, petId, clientId);

        var result = await CreateHandler().Handle(new GetLabResultByIdQuery(lr.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Id.Should().Be(lr.Id);
        dto.TenantId.Should().Be(tid);
        dto.ClinicId.Should().Be(clinicId);
        dto.PetId.Should().Be(petId);
        dto.ExaminationId.Should().Be(examId);
        dto.TestName.Should().Be("Test adı");
        dto.ResultText.Should().Be("Sonuç metni");
        dto.Interpretation.Should().Be("Yorum");
        dto.Notes.Should().Be("Not");
        dto.ResultDateUtc.Should().Be(lr.ResultDateUtc);
    }

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var labResultId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupLabResultFound(BuildLabResult(tenantId, clinicId, petId, labResultId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetLabResultByIdQuery(labResultId), token);

        _labResults.Verify(x => x.FirstOrDefaultAsync(It.IsAny<LabResultByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
