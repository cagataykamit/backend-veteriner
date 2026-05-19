using System.Security.Claims;
using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Auth.Commands.SelectClinic;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Constants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Commands;

public sealed class SelectClinicCommandHandlerTests
{
    private readonly Mock<IRefreshTokenRepository> _refreshRepo = new();
    private readonly Mock<ITokenHashService> _hash = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IJwtOptionsProvider> _opt = new();
    private readonly Mock<IPermissionReader> _permissionReader = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();

    private SelectClinicCommandHandler CreateHandler()
        => new(
            _refreshRepo.Object,
            _hash.Object,
            _jwt.Object,
            _opt.Object,
            _permissionReader.Object,
            _tenants.Object,
            _userTenants.Object,
            _userClinics.Object,
            _clinicsRead.Object,
            _userOperationClaims.Object);

    private (User user, RefreshToken stored) ArrangeRefreshAndTenant(Guid tenantId, bool tenantActive = true)
    {
        var user = new User("u@test.com", "h");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null, tenantId);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);

        _hash.Setup(h => h.ComputeSha256("raw")).Returns("hash");
        _refreshRepo
            .Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        if (!tenantActive)
            tenant.Deactivate();
        _tenants
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userTenants
            .Setup(r => r.ExistsAsync(user.Id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return (user, stored);
    }

    private void ArrangeClaimNames(Guid userId, params string[] names)
    {
        _userOperationClaims
            .Setup(r => r.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);
    }

    private static Clinic MakeClinic(Guid tenantId, Guid clinicId, bool isActive = true)
    {
        var clinic = new Clinic(tenantId, "K", "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);
        if (!isActive)
            clinic.Deactivate();
        return clinic;
    }

    private void ArrangeSuccessfulJwt(Guid userId, List<Claim> capturedClaims)
    {
        _permissionReader
            .Setup(p => p.GetPermissionsAsync(userId, It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "Permission.A", "Permission.B" });
        _opt.SetupGet(o => o.RefreshTokenDays).Returns(14);
        _jwt
            .Setup(j => j.Create(It.IsAny<User>(), It.IsAny<IEnumerable<Claim>?>()))
            .Returns<User, IEnumerable<Claim>?>((_, claims) =>
            {
                if (claims is not null)
                    capturedClaims.AddRange(claims);
                return ("access", "newRaw", DateTime.UtcNow.AddMinutes(30));
            });
        _hash.Setup(h => h.ComputeSha256("newRaw")).Returns("newHash");
        _refreshRepo
            .Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _refreshRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_Should_Fail_When_UserNotAssignedToClinic()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var (user, _) = ArrangeRefreshAndTenant(tid);
        ArrangeClaimNames(user.Id /* no tenant-wide claims */);

        var clinic = MakeClinic(tid, cid);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _userClinics
            .Setup(r => r.ExistsActiveInTenantAsync(user.Id, tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.UserClinicNotAssigned");
    }

    [Theory]
    [InlineData("ClinicAdmin")]
    [InlineData("Veteriner")]
    [InlineData("Sekreter")]
    public async Task Handle_Should_RequireAssignment_When_NonTenantWideClaim(string claimName)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var (user, _) = ArrangeRefreshAndTenant(tid);
        ArrangeClaimNames(user.Id, claimName);

        var clinic = MakeClinic(tid, cid);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _userClinics
            .Setup(r => r.ExistsActiveInTenantAsync(user.Id, tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.UserClinicNotAssigned");
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("PlatformAdmin")]
    [InlineData("admin")] // case-insensitive
    public async Task Handle_Should_SucceedWithoutAssignment_When_TenantWideClaim(string claimName)
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var (user, _) = ArrangeRefreshAndTenant(tid);
        ArrangeClaimNames(user.Id, claimName);

        var clinic = MakeClinic(tid, cid);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var capturedClaims = new List<Claim>();
        ArrangeSuccessfulJwt(user.Id, capturedClaims);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("access");
        result.Value.RefreshToken.Should().Be("newRaw");

        // Assignment kontrolünden tamamen kaçınılmalı.
        _userClinics.Verify(
            r => r.ExistsActiveInTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "tenant-wide kullanıcılar için UserClinic atama sorgusu yapılmamalı");

        // JWT clinic_id claim'i seçilen klinik olmalı (contract korunmuş).
        capturedClaims
            .Should()
            .Contain(c => c.Type == VeterinerClaims.ClinicId && c.Value == cid.ToString("D"));
        capturedClaims
            .Should()
            .Contain(c => c.Type == VeterinerClaims.TenantId && c.Value == tid.ToString("D"));
    }

    [Fact]
    public async Task Handle_Should_ReturnClinicNotFound_When_TenantWide_AndClinicInDifferentTenant()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var (user, _) = ArrangeRefreshAndTenant(tid);
        ArrangeClaimNames(user.Id, "Admin");

        // ClinicByIdSpec(sessionTenantId, clinicId) farklı tenant kliniğini bulamaz → null döner.
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.ClinicNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnClinicNotFound_When_TenantWide_AndClinicInactive()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var (user, _) = ArrangeRefreshAndTenant(tid);
        ArrangeClaimNames(user.Id, "Owner");

        var clinic = MakeClinic(tid, cid, isActive: false);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.ClinicNotFound");
    }

    [Fact]
    public async Task Handle_Should_ReturnClinicNotFound_When_NonTenantWide_AndClinicInactive()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var (user, _) = ArrangeRefreshAndTenant(tid);
        ArrangeClaimNames(user.Id, "ClinicAdmin");

        // Mevcut davranış: assignment yok + klinik pasif → ClinicNotFound (UserClinicNotAssigned değil).
        var clinic = MakeClinic(tid, cid, isActive: false);
        _clinicsRead
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);
        _userClinics
            .Setup(r => r.ExistsActiveInTenantAsync(user.Id, tid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.ClinicNotFound");
    }
}
