using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Clinics.Access;

public sealed class ClinicReadScopeResolverTests
{
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<IUserOperationClaimRepository> _claims = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();

    private ClinicReadScopeResolver CreateSut()
        => new(
            _clientContext.Object,
            new ClinicAssignmentAccessGuard(_claims.Object),
            _userClinics.Object,
            _clinics.Object);

    private void SetupUser(Guid userId)
    {
        _clientContext.SetupGet(x => x.UserId).Returns(userId);
    }

    private void SetupClaims(Guid userId, params string[] names)
    {
        _claims.Setup(x => x.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);
    }

    private static Clinic BuildClinic(Guid tenantId, Guid clinicId, string name = "Klinik")
    {
        var clinic = new Clinic(tenantId, name, "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);
        return clinic;
    }

    private void SetupAccessibleClinics(Guid userId, Guid tenantId, params Clinic[] clinics)
    {
        _userClinics
            .Setup(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinics);
    }

    private void SetupClinicInTenant(Guid tenantId, Guid clinicId, Clinic? clinic = null)
    {
        _clinics
            .Setup(x => x.FirstOrDefaultAsync(
                It.Is<ClinicByIdSpec>(s => true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicByIdSpec _, CancellationToken __) =>
            {
                if (clinic is not null)
                    return clinic;
                return BuildClinic(tenantId, clinicId);
            });
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("PlatformAdmin")]
    public async Task TenantWideUser_ExplicitTenantClinicId_Should_Succeed_WithoutUserClinicCheck(string claim)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, claim);
        SetupClinicInTenant(tenantId, clinicId);

        var result = await CreateSut().ResolveAsync(tenantId, clinicId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SingleClinicId.Should().Be(clinicId);
        result.Value.AccessibleClinicIds.Should().BeNull();
        _userClinics.Verify(
            x => x.ListAccessibleClinicsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public async Task NonTenantWideUser_ExplicitAssignedClinicId_Should_Succeed(string claim)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, claim);
        SetupAccessibleClinics(userId, tenantId, BuildClinic(tenantId, clinicId));

        var result = await CreateSut().ResolveAsync(tenantId, clinicId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SingleClinicId.Should().Be(clinicId);
        result.Value.AccessibleClinicIds.Should().BeNull();
    }

    [Theory]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public async Task NonTenantWideUser_ExplicitUnassignedClinicId_Should_FailWithAccessDenied(string claim)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();
        var unassignedId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, claim);
        SetupAccessibleClinics(userId, tenantId, BuildClinic(tenantId, assignedId));

        var result = await CreateSut().ResolveAsync(tenantId, unassignedId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task ClinicAdmin_ExplicitAssignedClinicId_Should_Succeed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, "ClinicAdmin");
        SetupAccessibleClinics(userId, tenantId, BuildClinic(tenantId, clinicId));

        var result = await CreateSut().ResolveAsync(tenantId, clinicId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SingleClinicId.Should().Be(clinicId);
    }

    [Fact]
    public async Task ClinicAdmin_ExplicitUnassignedClinicId_Should_FailWithAccessDenied()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();
        var unassignedId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, "ClinicAdmin");
        SetupAccessibleClinics(userId, tenantId, BuildClinic(tenantId, assignedId));

        var result = await CreateSut().ResolveAsync(tenantId, unassignedId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("PlatformAdmin")]
    public async Task TenantWideUser_NullRequestClinicId_Should_ReturnTenantWideScope(string claim)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, claim);

        var result = await CreateSut().ResolveAsync(tenantId, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SingleClinicId.Should().BeNull();
        result.Value.AccessibleClinicIds.Should().BeNull();
        _userClinics.Verify(
            x => x.ListAccessibleClinicsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NonTenantWideUser_NullRequestClinicId_Should_ReturnAccessibleClinicIdsFromUserClinic()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, "Veteriner");
        SetupAccessibleClinics(
            userId,
            tenantId,
            BuildClinic(tenantId, c1, "A"),
            BuildClinic(tenantId, c2, "B"));

        var result = await CreateSut().ResolveAsync(tenantId, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SingleClinicId.Should().BeNull();
        result.Value.AccessibleClinicIds.Should().BeEquivalentTo(new[] { c1, c2 });
    }

    [Fact]
    public async Task NonTenantWideUser_NullRequestClinicId_AndNoAssignments_Should_NotReturnTenantWideScope()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, "Sekreter");
        SetupAccessibleClinics(userId, tenantId);

        var result = await CreateSut().ResolveAsync(tenantId, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SingleClinicId.Should().BeNull();
        result.Value.AccessibleClinicIds.Should().NotBeNull();
        result.Value.AccessibleClinicIds.Should().BeEmpty();
    }

    [Fact]
    public async Task TenantWideUser_CrossTenantClinicId_Should_FailWithNotFound()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var foreignClinicId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, "Admin");
        _clinics
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateSut().ResolveAsync(tenantId, foreignClinicId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.NotFound");
    }

    [Fact]
    public async Task NonTenantWideUser_CrossTenantClinicId_Should_FailWithAccessDenied()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();
        var foreignClinicId = Guid.NewGuid();
        SetupUser(userId);
        SetupClaims(userId, "Veteriner");
        SetupAccessibleClinics(userId, tenantId, BuildClinic(tenantId, assignedId));

        var result = await CreateSut().ResolveAsync(tenantId, foreignClinicId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Clinics.AccessDenied");
    }

    [Fact]
    public async Task Resolve_Should_Fail_When_UserContextMissing()
    {
        _clientContext.SetupGet(x => x.UserId).Returns((Guid?)null);

        var result = await CreateSut().ResolveAsync(Guid.NewGuid(), null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.Unauthorized.UserContextMissing");
    }

    [Fact]
    public async Task Resolve_Should_PropagateCancellationToken_ToAllAsyncDependencies()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var ct = new CancellationTokenSource().Token;
        SetupUser(userId);
        SetupClaims(userId, "Veteriner");
        SetupAccessibleClinics(userId, tenantId, BuildClinic(tenantId, clinicId));

        await CreateSut().ResolveAsync(tenantId, clinicId, ct);

        _claims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(userId, ct), Times.Once);
        _userClinics.Verify(x => x.ListAccessibleClinicsAsync(userId, tenantId, null, ct), Times.Once);
    }

    [Fact]
    public async Task Resolve_Should_PropagateCancellationToken_ToClinicRepository_ForTenantWideUser()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        var ct = new CancellationTokenSource().Token;
        SetupUser(userId);
        SetupClaims(userId, "Admin");
        SetupClinicInTenant(tenantId, clinicId);

        await CreateSut().ResolveAsync(tenantId, clinicId, ct);

        _claims.Verify(x => x.GetOperationClaimNamesByUserIdAsync(userId, ct), Times.Once);
        _clinics.Verify(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), ct), Times.Once);
    }
}
