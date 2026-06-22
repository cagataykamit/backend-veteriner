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
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Treatment>> _treatments = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetTreatmentByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetTreatmentByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _treatments.Object,
            _pets.Object,
            _clients.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Treatment BuildTreatment(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? treatmentId = null,
        Guid? examinationId = null)
    {
        var tr = new Treatment(
            tenantId,
            clinicId,
            petId,
            examinationId,
            DateTime.UtcNow.AddDays(-1),
            "Başlık",
            "Uzun açıklama",
            "Notlar",
            DateTime.UtcNow.AddDays(5));
        if (treatmentId.HasValue)
            typeof(Treatment).GetProperty(nameof(Treatment.Id))!.SetValue(tr, treatmentId.Value);
        return tr;
    }

    private void SetupTreatmentFound(Treatment treatment)
        => _treatments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(treatment);

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
        SetupTenant(tid);
        _treatments.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment?)null);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_TreatmentBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _treatments.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Treatment?)null);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideAdminReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupTreatmentFound(BuildTreatment(tenantId, clinicId, petId, treatmentId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(treatmentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(treatmentId);
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
        var treatmentId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Owner");
        SetupTreatmentFound(BuildTreatment(tenantId, clinicId, petId, treatmentId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(treatmentId), CancellationToken.None);

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
        SetupTenant(tenantId);
        SetupTenantWideRole("PlatformAdmin");
        var entity = BuildTreatment(tenantId, clinicId, petId);
        SetupTreatmentFound(entity);
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Title.Should().Be("Başlık");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.ClientName.Should().Be("Ali Veli");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicTreatment()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupTreatmentFound(BuildTreatment(tenantId, clinicId, petId, treatmentId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(treatmentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToTreatmentClinic_WithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupAssignment(clinicId, assigned: false);
        SetupTreatmentFound(BuildTreatment(tenantId, clinicId, petId, treatmentId));

        var result = await CreateHandler().Handle(new GetTreatmentByIdQuery(treatmentId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Treatments.NotFound");
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

        var tr = BuildTreatment(tid, rowClinicId, petId);
        SetupTreatmentFound(tr);

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

        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        SetupTenantWideRole("Admin");

        var tr = BuildTreatment(tid, clinicId, petId, examinationId: examId);
        SetupTreatmentFound(tr);
        SetupDetailGraph(tid, petId, clientId);

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
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var treatmentId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupTreatmentFound(BuildTreatment(tenantId, clinicId, petId, treatmentId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetTreatmentByIdQuery(treatmentId), token);

        _treatments.Verify(x => x.FirstOrDefaultAsync(It.IsAny<TreatmentByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
