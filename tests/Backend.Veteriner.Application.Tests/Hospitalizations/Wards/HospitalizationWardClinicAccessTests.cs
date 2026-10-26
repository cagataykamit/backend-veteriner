using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Hospitalizations.Wards.Access;
using Backend.Veteriner.Domain.Clinics;
using AwesomeAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Hospitalizations.Wards;

/// <summary>Bölüm/oda tanımları klinik erişim kuralı (çalışma saatleri deseni): izin, bağlam, klinik, atanmış klinik.</summary>
public sealed class HospitalizationWardClinicAccessTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IClinicAssignmentAccessGuard> _assignmentGuard = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    public HospitalizationWardClinicAccessTests()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(_userId);
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(true);
        _assignmentGuard.Setup(x => x.MustApplyAssignedClinicScopeAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var clinic = new Clinic(_tenantId, "K", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, _clinicId);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
    }

    private HospitalizationWardClinicAccess Create()
        => new(_tenantContext.Object, _clientContext.Object, _permissions.Object, _assignmentGuard.Object, _userClinics.Object, _clinics.Object);

    [Fact]
    public async Task Write_Should_Return_PermissionDenied_Before_Any_Lookup()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await Create().EnsureWriteAccessAsync(_clinicId, CancellationToken.None);

        result.Error.Code.Should().Be("Auth.PermissionDenied");
        _clinics.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Read_Should_Not_Require_Clinics_Update()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Clinics.Update)).Returns(false);

        var result = await Create().EnsureReadAccessAsync(_clinicId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(_tenantId);
    }

    [Fact]
    public async Task Should_Fail_When_Tenant_Or_User_Context_Missing()
    {
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);
        (await Create().EnsureWriteAccessAsync(_clinicId, CancellationToken.None)).Error.Code.Should().Be("Tenants.ContextMissing");

        _tenantContext.SetupGet(x => x.TenantId).Returns(_tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns((Guid?)null);
        (await Create().EnsureWriteAccessAsync(_clinicId, CancellationToken.None)).Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Should_Return_ClinicNotFound_When_Clinic_Not_In_Tenant()
    {
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>())).ReturnsAsync((Clinic?)null);

        var result = await Create().EnsureWriteAccessAsync(_clinicId, CancellationToken.None);

        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task Should_Return_AccessDenied_For_Scoped_User_Without_Assignment_And_Succeed_With_It()
    {
        _assignmentGuard.Setup(x => x.MustApplyAssignedClinicScopeAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _userClinics.Setup(x => x.ExistsAsync(_userId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        (await Create().EnsureWriteAccessAsync(_clinicId, CancellationToken.None)).Error.Code.Should().Be("Clinics.AccessDenied");

        _userClinics.Setup(x => x.ExistsAsync(_userId, _clinicId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var ok = await Create().EnsureWriteAccessAsync(_clinicId, CancellationToken.None);
        ok.IsSuccess.Should().BeTrue();
        ok.Value.Should().Be(_tenantId);
    }
}
