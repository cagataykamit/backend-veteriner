using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Commands.Activate;
using Backend.Veteriner.Application.Clinics.Commands.Deactivate;
using Backend.Veteriner.Application.Clinics.Commands.Update;
using Backend.Veteriner.Application.Clinics.Commands.Update.Validators;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Handlers;

/// <summary>
/// Faz 5A: tenant-scoped klinik güncelleme + activate/deactivate.
/// Ortak davranışlar: yetki (Clinics.Update), tenant context, başka tenant kliniği -> Clinics.NotFound.
/// Update: case-insensitive duplicate -> Clinics.DuplicateName; kendi Id'si duplicate sayılmaz.
/// Deactivate/Activate: idempotent -> AlreadyInactive/AlreadyActive = true olduğunda SaveChanges çağrılmaz.
/// ReadOnly/Cancelled tenant davranışı merkezi <c>TenantSubscriptionWriteGuardBehavior</c> üzerinden işler
/// (ayrı handler testi gerekmez; Faz 3B/4B ile aynı çizgi).
/// </summary>
public sealed class ClinicSettingsHandlersTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IRepository<Clinic>> _clinicsWrite = new();

    private UpdateClinicCommandHandler CreateUpdateHandler()
        => new(_tenantContext.Object, _permissions.Object, _clinicsRead.Object, _clinicsWrite.Object);

    private DeactivateClinicCommandHandler CreateDeactivateHandler()
        => new(_tenantContext.Object, _permissions.Object, _clinicsRead.Object, _clinicsWrite.Object);

    private ActivateClinicCommandHandler CreateActivateHandler()
        => new(_tenantContext.Object, _permissions.Object, _clinicsRead.Object, _clinicsWrite.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId, string name = "Merkez", string city = "İstanbul", bool active = true)
    {
        var clinic = new Clinic(tenantId, name, city);
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        if (!active) clinic.Deactivate();
        return clinic;
    }

    private void AllowClinicsUpdate() =>
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(true);

    // =========================================================================
    // UPDATE
    // =========================================================================

    [Fact]
    public async Task Update_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await CreateUpdateHandler().Handle(
            new UpdateClinicCommand(Guid.NewGuid(), "Yeni Ad", "Ankara"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _clinicsRead.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnFailure_When_ContextMissing()
    {
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdateClinicCommand(Guid.NewGuid(), "Yeni Ad", "Ankara"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
        _clinicsRead.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnFailure_When_ClinicNotFound_InTenant()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdateClinicCommand(clinicId, "Yeni Ad", "Ankara"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_ReturnFailure_When_DuplicateName_InSameTenant_ForDifferentClinic()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var otherClinicId = Guid.NewGuid();
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(clinicId, tenantId, "Merkez"));
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByTenantAndNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(otherClinicId, tenantId, "ŞUBE"));

        var result = await CreateUpdateHandler().Handle(
            new UpdateClinicCommand(clinicId, "Şube", "İzmir"),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.DuplicateName");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_DuplicateSpecReturnsSameClinic_CaseOnlyChange()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var existing = BuildClinic(clinicId, tenantId, "Merkez", "İstanbul");
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByTenantAndNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await CreateUpdateHandler().Handle(
            new UpdateClinicCommand(clinicId, "MERKEZ", "Ankara"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("MERKEZ");
        result.Value.City.Should().Be("Ankara");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Should_Succeed_When_Valid()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var existing = BuildClinic(clinicId, tenantId, "Merkez", "İstanbul");
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByTenantAndNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateUpdateHandler().Handle(
            new UpdateClinicCommand(clinicId, "Yeni Ad", "Ankara"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(clinicId);
        result.Value.TenantId.Should().Be(tenantId);
        result.Value.Name.Should().Be("Yeni Ad");
        result.Value.City.Should().Be("Ankara");
        result.Value.IsActive.Should().BeTrue();
        existing.Name.Should().Be("Yeni Ad");
        existing.City.Should().Be("Ankara");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Update_Validator_Should_FailOnEmptyOrShortInputs()
    {
        var validator = new UpdateClinicCommandValidator();

        validator.Validate(new UpdateClinicCommand(Guid.Empty, "Ok", "Ankara"))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateClinicCommand(Guid.NewGuid(), "", "Ankara"))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateClinicCommand(Guid.NewGuid(), "A", "Ankara"))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateClinicCommand(Guid.NewGuid(), "Merkez", ""))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateClinicCommand(Guid.NewGuid(), "Merkez", "A"))
            .IsValid.Should().BeFalse();
        validator.Validate(new UpdateClinicCommand(Guid.NewGuid(), "Merkez", "Ankara"))
            .IsValid.Should().BeTrue();
    }

    // =========================================================================
    // DEACTIVATE
    // =========================================================================

    [Fact]
    public async Task Deactivate_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await CreateDeactivateHandler().Handle(
            new DeactivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivate_Should_ReturnFailure_When_ContextMissing()
    {
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateDeactivateHandler().Handle(
            new DeactivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Deactivate_Should_ReturnFailure_When_ClinicNotFound_InTenant()
    {
        var tenantId = Guid.NewGuid();
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateDeactivateHandler().Handle(
            new DeactivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivate_Should_Succeed_When_Active()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clinic = BuildClinic(clinicId, tenantId, active: true);
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateDeactivateHandler().Handle(
            new DeactivateClinicCommand(clinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        result.Value.AlreadyInactive.Should().BeFalse();
        clinic.IsActive.Should().BeFalse();
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_Should_BeIdempotent_When_AlreadyInactive()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clinic = BuildClinic(clinicId, tenantId, active: false);
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateDeactivateHandler().Handle(
            new DeactivateClinicCommand(clinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyInactive.Should().BeTrue();
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // ACTIVATE
    // =========================================================================

    [Fact]
    public async Task Activate_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await CreateActivateHandler().Handle(
            new ActivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_Should_ReturnFailure_When_ContextMissing()
    {
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateActivateHandler().Handle(
            new ActivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Activate_Should_ReturnFailure_When_ClinicNotFound_InTenant()
    {
        var tenantId = Guid.NewGuid();
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateActivateHandler().Handle(
            new ActivateClinicCommand(Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_Should_Succeed_When_Inactive()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clinic = BuildClinic(clinicId, tenantId, active: false);
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateActivateHandler().Handle(
            new ActivateClinicCommand(clinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ClinicId.Should().Be(clinicId);
        result.Value.AlreadyActive.Should().BeFalse();
        clinic.IsActive.Should().BeTrue();
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Activate_Should_BeIdempotent_When_AlreadyActive()
    {
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var clinic = BuildClinic(clinicId, tenantId, active: true);
        AllowClinicsUpdate();
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateActivateHandler().Handle(
            new ActivateClinicCommand(clinicId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyActive.Should().BeTrue();
        _clinicsWrite.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
