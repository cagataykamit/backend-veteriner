using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.AssignMemberClinic;
using Backend.Veteriner.Application.Tenants.Commands.RemoveMemberClinic;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Faz 4B: tenant-scoped klinik üyelik ata/çıkar.
/// Ortak davranışlar: yetki / context / tenant match / member-in-tenant / clinic not-found.
/// Assign: pasif klinik reddedilir (Clinics.Inactive). Idempotent → AlreadyAssigned=true, repo yazmaz, commit yok.
/// Remove: self-protect Clinics.SelfClinicRemoveForbidden. Pasif klinik remove akışını engellemez.
/// Idempotent → AlreadyRemoved=true, repo silmez, commit yok.
/// Permission cache invalidation yapılmaz: clinic membership permission setini değiştirmez.
/// </summary>
public sealed class TenantMemberClinicAssignmentHandlersTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IUserTenantRepository> _userTenantRepo = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AssignTenantMemberClinicCommandHandler CreateAssignHandler()
        => new(_tenantContext.Object, _permissions.Object, _userTenantRepo.Object,
               _clinicsRead.Object, _userClinics.Object, _uow.Object);

    private RemoveTenantMemberClinicCommandHandler CreateRemoveHandler()
        => new(_tenantContext.Object, _clientContext.Object, _permissions.Object, _userTenantRepo.Object,
               _clinicsRead.Object, _userClinics.Object, _uow.Object);

    private static Clinic BuildClinic(Guid id, Guid tenantId, string name = "Merkez Klinik", bool active = true)
    {
        var clinic = new Clinic(tenantId, name, "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, id);
        if (!active) clinic.Deactivate();
        return clinic;
    }

    private void SetupHappyBasics(Guid tenantId, Guid memberId)
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _userTenantRepo.Setup(x => x.ExistsAsync(memberId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    // -------------------------------------------------------------------
    // ASSIGN
    // -------------------------------------------------------------------

    [Fact]
    public async Task Assign_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _userTenantRepo.Verify(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Assign_Should_ReturnFailure_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Assign_Should_Return_NotFound_When_Member_Not_In_Tenant()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(tid, mid, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Members.NotFound");
        _clinicsRead.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Should_Return_ClinicNotFound_When_Clinic_Missing()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        _userClinics.Verify(x => x.AddAsync(It.IsAny<UserClinic>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Should_Return_Inactive_When_Clinic_Deactivated()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid, active: false));

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.Inactive");
        _userClinics.Verify(x => x.AddAsync(It.IsAny<UserClinic>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Should_Add_On_HappyPath()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid, "Ankara Şubesi"));
        _userClinics.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(mid);
        result.Value.ClinicId.Should().Be(cid);
        result.Value.ClinicName.Should().Be("Ankara Şubesi");
        result.Value.AlreadyAssigned.Should().BeFalse();

        _userClinics.Verify(x => x.AddAsync(
            It.Is<UserClinic>(e => e.UserId == mid && e.ClinicId == cid),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Assign_Should_Be_Idempotent_When_Already_Assigned()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid, "Merkez"));
        _userClinics.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyAssigned.Should().BeTrue();
        result.Value.ClinicName.Should().Be("Merkez");

        _userClinics.Verify(x => x.AddAsync(It.IsAny<UserClinic>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // -------------------------------------------------------------------
    // REMOVE
    // -------------------------------------------------------------------

    [Fact]
    public async Task Remove_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Remove_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Remove_Should_ReturnFailure_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Remove_Should_Block_Self_Clinic_Removal()
    {
        var tid = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(callerId);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(tid, callerId, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.SelfClinicRemoveForbidden");

        _userTenantRepo.Verify(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userClinics.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_Should_Return_NotFound_When_Member_Not_In_Tenant()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid()); // different from mid
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(tid, mid, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Members.NotFound");
    }

    [Fact]
    public async Task Remove_Should_Return_ClinicNotFound_When_Clinic_Missing()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(tid, mid, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
        _userClinics.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_Should_Remove_On_HappyPath()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid, "Bakırköy"));
        _userClinics.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(mid);
        result.Value.ClinicId.Should().Be(cid);
        result.Value.AlreadyRemoved.Should().BeFalse();

        _userClinics.Verify(x => x.RemoveAsync(mid, cid, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Should_Work_On_Inactive_Clinic_To_Clean_Up_Misassignment()
    {
        // Pasif klinik remove akışını engellemez: yanlış ataması olan üyenin temizlenmesi mümkün olmalı.
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid, "Kapalı Şube", active: false));
        _userClinics.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyRemoved.Should().BeFalse();
        _userClinics.Verify(x => x.RemoveAsync(mid, cid, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Should_Be_Idempotent_When_Already_Removed()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClinic(cid, tid));
        _userClinics.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberClinicCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyRemoved.Should().BeTrue();

        _userClinics.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
