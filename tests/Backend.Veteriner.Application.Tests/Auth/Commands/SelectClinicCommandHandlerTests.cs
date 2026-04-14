using Backend.Veteriner.Application.Auth.Contracts;
using Backend.Veteriner.Application.Auth.Commands.SelectClinic;
using Backend.Veteriner.Application.Common.Abstractions;
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
            _clinicsRead.Object);

    [Fact]
    public async Task Handle_Should_Fail_When_UserNotAssignedToClinic()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var user = new User("u@test.com", "h");
        var stored = new RefreshToken(user.Id, "hash", DateTime.UtcNow.AddDays(1), null, null, tid);
        typeof(RefreshToken).GetProperty(nameof(RefreshToken.User))!.SetValue(stored, user);

        _hash.Setup(h => h.ComputeSha256("raw")).Returns("hash");
        _refreshRepo.Setup(r => r.GetByHashAsync("hash", It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var tenant = new Tenant("T");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tid);
        _tenants.Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _userTenants.Setup(r => r.ExistsAsync(user.Id, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var clinic = new Clinic(tid, "K", "Istanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinicsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Application.Clinics.Specs.ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        _userClinics.Setup(r => r.ExistsActiveInTenantAsync(user.Id, tid, cid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(new SelectClinicCommand("raw", cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.UserClinicNotAssigned");
    }
}
