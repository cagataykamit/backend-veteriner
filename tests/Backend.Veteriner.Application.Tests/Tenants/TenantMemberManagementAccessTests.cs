using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants;

/// <summary>
/// Tam test 2026-11-22 kararı: klinik yöneticisi kendi kliniğine personel davet eder ve üyeleri yönetir, işletme yöneticisi (Admin) rolünü
/// atayamaz; hekim yeni müşteri ve hasta açar. Rol izinleri seed'de (<see cref="RolePermissionBindings"/>).
/// </summary>
public sealed class TenantMemberManagementAccessTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _guard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IUserOperationClaimRepository> _claims = new();

    private TenantMemberManagementAccess Access(bool clinicScoped)
    {
        _client.SetupGet(c => c.UserId).Returns(_userId);
        _guard.Setup(g => g.MustApplyAssignedClinicScopeAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(clinicScoped);
        return new TenantMemberManagementAccess(_client.Object, _guard.Object, _userClinics.Object, _claims.Object);
    }

    [Theory]
    [InlineData("ClinicAdmin", true)]
    [InlineData("Admin", true)]
    [InlineData("Owner", true)]
    [InlineData("Veteriner", false)]
    [InlineData("Sekreter", false)]
    public void Invite_Permission_Should_Be_Given_To_Admin_Levels(string role, bool expected)
        => RolePermissionBindings.Map[role].Contains(PermissionCatalog.Tenants.InviteCreate).Should().Be(expected);

    [Theory]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    [InlineData("ClinicAdmin")]
    public void Clinical_Roles_Should_Create_Clients_And_Pets(string role)
        => RolePermissionBindings.Map[role].Should().Contain([PermissionCatalog.Clients.Create, PermissionCatalog.Pets.Create]);

    [Fact]
    public void Veteriner_Should_Still_Not_Update_Clients()
        => RolePermissionBindings.Map["Veteriner"].Should().NotContain(PermissionCatalog.Clients.Update);

    [Theory]
    [InlineData("Admin", true, false)]
    [InlineData("Admin", false, true)]
    [InlineData("ClinicAdmin", true, true)]
    [InlineData("Veteriner", true, true)]
    [InlineData("Sekreter", true, true)]
    [InlineData("PlatformAdmin", false, false)]
    public void ClinicAdmin_Should_Not_Assign_Tenant_Admin_Role(string role, bool actorScoped, bool expected)
        => TenantMemberManagementAccess.CanAssignRole(role, actorScoped).Should().Be(expected);

    [Fact]
    public async Task ClinicAdmin_Should_Invite_Only_To_Assigned_Clinic()
    {
        var access = Access(clinicScoped: true);
        _userClinics.Setup(u => u.ExistsAsync(_userId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await access.EnsureClinicAsync(_clinicId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        var other = await access.EnsureClinicAsync(Guid.NewGuid(), CancellationToken.None);
        other.IsSuccess.Should().BeFalse();
        other.Error.Code.Should().Be(TenantMemberManagementAccess.ClinicAccessDeniedCode);
    }

    [Fact]
    public async Task Tenant_Admin_Should_Manage_Any_Clinic_And_Settings()
    {
        var access = Access(clinicScoped: false);

        (await access.EnsureClinicAsync(Guid.NewGuid(), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await access.EnsureRoleAsync("Admin", CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await access.EnsureTenantSettingsAsync(CancellationToken.None)).IsSuccess.Should().BeTrue();
        _userClinics.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClinicAdmin_Should_Not_Touch_Tenant_Admin_Member_Or_Settings()
    {
        var access = Access(clinicScoped: true);
        var admin = Guid.NewGuid();
        var vet = Guid.NewGuid();
        _claims.Setup(c => c.GetOperationClaimNamesByUserIdAsync(admin, It.IsAny<CancellationToken>())).ReturnsAsync(["Admin"]);
        _claims.Setup(c => c.GetOperationClaimNamesByUserIdAsync(vet, It.IsAny<CancellationToken>())).ReturnsAsync(["Veteriner"]);

        (await access.EnsureMemberAsync(admin, CancellationToken.None)).Error.Code.Should().Be(TenantMemberManagementAccess.TargetIsTenantAdminCode);
        (await access.EnsureMemberAsync(vet, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await access.EnsureRoleAsync("Admin", CancellationToken.None)).Error.Code.Should().Be(TenantMemberManagementAccess.RoleNotAssignableCode);
        (await access.EnsureRoleAsync("Sekreter", CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await access.EnsureTenantSettingsAsync(CancellationToken.None)).Error.Code.Should().Be(TenantMemberManagementAccess.TenantSettingsDeniedCode);
    }
}
