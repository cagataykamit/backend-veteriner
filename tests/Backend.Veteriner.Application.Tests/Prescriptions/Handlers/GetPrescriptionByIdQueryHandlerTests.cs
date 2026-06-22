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
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Prescription>> _prescriptions = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetPrescriptionByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetPrescriptionByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _prescriptions.Object,
            _pets.Object,
            _clients.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Prescription BuildPrescription(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? prescriptionId = null,
        Guid? examinationId = null,
        Guid? treatmentId = null)
    {
        var pr = new Prescription(
            tenantId,
            clinicId,
            petId,
            examinationId,
            treatmentId,
            DateTime.UtcNow.AddDays(-1),
            "Başlık",
            "İçerik gövdesi",
            "Notlar",
            DateTime.UtcNow.AddDays(3));
        if (prescriptionId.HasValue)
            typeof(Prescription).GetProperty(nameof(Prescription.Id))!.SetValue(pr, prescriptionId.Value);
        return pr;
    }

    private void SetupPrescriptionFound(Prescription prescription)
        => _prescriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prescription);

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
        SetupTenant(tid);
        _prescriptions.Setup(r => r.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_PrescriptionBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _prescriptions.Setup(x => x.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Prescription?)null);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideAdminReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupPrescriptionFound(BuildPrescription(tenantId, clinicId, petId, prescriptionId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(prescriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(prescriptionId);
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
        var prescriptionId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Owner");
        SetupPrescriptionFound(BuildPrescription(tenantId, clinicId, petId, prescriptionId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(prescriptionId), CancellationToken.None);

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
        var entity = BuildPrescription(tenantId, clinicId, petId);
        SetupPrescriptionFound(entity);
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Title.Should().Be("Başlık");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.ClientName.Should().Be("Ali Veli");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicPrescription()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupPrescriptionFound(BuildPrescription(tenantId, clinicId, petId, prescriptionId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(prescriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToPrescriptionClinic_WithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupAssignment(clinicId, assigned: false);
        SetupPrescriptionFound(BuildPrescription(tenantId, clinicId, petId, prescriptionId));

        var result = await CreateHandler().Handle(new GetPrescriptionByIdQuery(prescriptionId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Prescriptions.NotFound");
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

        var pr = BuildPrescription(tid, rowClinicId, petId);
        SetupPrescriptionFound(pr);

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

        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        SetupTenantWideRole("Admin");

        var pr = BuildPrescription(tid, clinicId, petId, examinationId: examId, treatmentId: treatmentId);
        SetupPrescriptionFound(pr);
        SetupDetailGraph(tid, petId, clientId);

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
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var prescriptionId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupPrescriptionFound(BuildPrescription(tenantId, clinicId, petId, prescriptionId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetPrescriptionByIdQuery(prescriptionId), token);

        _prescriptions.Verify(x => x.FirstOrDefaultAsync(It.IsAny<PrescriptionByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
