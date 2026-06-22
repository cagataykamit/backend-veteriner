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
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Hospitalization>> _hospitalizations = new();
    private readonly Mock<IReadRepository<Pet>> _pets = new();
    private readonly Mock<IReadRepository<Client>> _clients = new();

    private readonly Guid _userId = Guid.NewGuid();

    public GetHospitalizationByIdQueryHandlerTests()
    {
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _userOperationClaims
            .Setup(x => x.GetOperationClaimNamesByUserIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
    }

    private GetHospitalizationByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clinicContext.Object,
            _clientContext.Object,
            _userOperationClaims.Object,
            _userClinics.Object,
            _hospitalizations.Object,
            _pets.Object,
            _clients.Object);

    private void SetupTenant(Guid tenantId) => _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);

    private static Hospitalization BuildHospitalization(
        Guid tenantId,
        Guid clinicId,
        Guid petId,
        Guid? hospitalizationId = null,
        DateTime? dischargedAtUtc = null)
    {
        var h = new Hospitalization(
            tenantId,
            clinicId,
            petId,
            null,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(2),
            "Sebep",
            "Not");
        if (hospitalizationId.HasValue)
            typeof(Hospitalization).GetProperty(nameof(Hospitalization.Id))!.SetValue(h, hospitalizationId.Value);
        if (dischargedAtUtc.HasValue)
            typeof(Hospitalization).GetProperty(nameof(Hospitalization.DischargedAtUtc))!.SetValue(h, dischargedAtUtc.Value);
        return h;
    }

    private void SetupHospitalizationFound(Hospitalization hospitalization)
        => _hospitalizations.Setup(x => x.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hospitalization);

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
        SetupTenant(tid);
        _hospitalizations.Setup(r => r.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_HospitalizationBelongsToOtherTenant()
    {
        var tenantId = Guid.NewGuid();
        SetupTenant(tenantId);
        _hospitalizations.Setup(x => x.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Hospitalization?)null);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_TenantWideAdminReadsWithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var hospitalizationId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Admin");
        SetupHospitalizationFound(BuildHospitalization(tenantId, clinicId, petId, hospitalizationId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(hospitalizationId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(hospitalizationId);
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
        var hospitalizationId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupTenantWideRole("Owner");
        SetupHospitalizationFound(BuildHospitalization(tenantId, clinicId, petId, hospitalizationId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(hospitalizationId), CancellationToken.None);

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
        var entity = BuildHospitalization(tenantId, clinicId, petId);
        SetupHospitalizationFound(entity);
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(entity.Id);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Reason.Should().Be("Sebep");
        result.Value.PetName.Should().Be("Pamuk");
        result.Value.ClientName.Should().Be("Ali Veli");
    }

    [Fact]
    public async Task Handle_Should_ReturnDetail_When_AssignedNonTenantWideUserReadsOwnClinicHospitalization()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var hospitalizationId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupHospitalizationFound(BuildHospitalization(tenantId, clinicId, petId, hospitalizationId));
        SetupDetailGraph(tenantId, petId, clientId);

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(hospitalizationId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToHospitalizationClinic_WithoutActiveClinicContext()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hospitalizationId = Guid.NewGuid();
        SetupTenant(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);
        SetupAssignment(clinicId, assigned: false);
        SetupHospitalizationFound(BuildHospitalization(tenantId, clinicId, petId, hospitalizationId));

        var result = await CreateHandler().Handle(new GetHospitalizationByIdQuery(hospitalizationId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Hospitalizations.NotFound");
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

        var h = BuildHospitalization(tid, rowClinicId, petId);
        SetupHospitalizationFound(h);

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

        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        SetupTenantWideRole("Admin");

        var h = BuildHospitalization(tid, clinicId, petId);
        SetupHospitalizationFound(h);
        SetupDetailGraph(tid, petId, clientId);

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

        SetupTenant(tid);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);
        SetupTenantWideRole("Admin");

        var h = BuildHospitalization(
            tid,
            clinicId,
            petId,
            dischargedAtUtc: DateTime.UtcNow.AddDays(-1));

        SetupHospitalizationFound(h);

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

    [Fact]
    public async Task CancellationToken_Should_BePassed_ToRepositoryCalls()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var petId = Guid.NewGuid();
        var hospitalizationId = Guid.NewGuid();
        SetupTenant(tenantId);
        SetupAssignment(clinicId, assigned: true);
        SetupHospitalizationFound(BuildHospitalization(tenantId, clinicId, petId, hospitalizationId));
        SetupDetailGraph(tenantId, petId, Guid.NewGuid());

        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        await CreateHandler().Handle(new GetHospitalizationByIdQuery(hospitalizationId), token);

        _hospitalizations.Verify(x => x.FirstOrDefaultAsync(It.IsAny<HospitalizationByIdSpec>(), token), Times.Once);
        _userOperationClaims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(_userId, token), Times.Once);
        _userClinics.Verify(x => x.ExistsAsync(_userId, clinicId, token), Times.Once);
    }
}
