using System.Reflection;
using Backend.Veteriner.Application.Auth.Queries.Me;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Auth.Queries;

public sealed class GetAccountSummaryQueryHandlerTests
{
    private readonly Mock<IClientContext> _client = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClinicContext> _clinicContext = new();
    private readonly Mock<IUserReadRepository> _users = new();
    private readonly Mock<IReadRepository<Tenant>> _tenants = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();

    private GetAccountSummaryQueryHandler CreateHandler()
        => new(
            _client.Object,
            _tenantContext.Object,
            _clinicContext.Object,
            _users.Object,
            _tenants.Object,
            _clinics.Object,
            _userOperationClaims.Object);

    private void ArrangeUser(Guid userId, string email = "cagatay.kamit@example.com")
    {
        _client.SetupGet(c => c.UserId).Returns(userId);

        var user = new User(email, "hash");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, userId);

        _users
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<UserByIdWithRolesSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
    }

    private void ArrangeClaimNames(Guid userId, params string[] names)
    {
        _userOperationClaims
            .Setup(r => r.GetOperationClaimNamesByUserIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(names);
    }

    [Fact]
    public async Task Handle_Should_ReturnBasicUserInfo_When_UserExists()
    {
        var userId = Guid.NewGuid();
        ArrangeUser(userId);
        ArrangeClaimNames(userId);

        var result = await CreateHandler().Handle(new GetAccountSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(userId);
        result.Value.Email.Should().Be("cagatay.kamit@example.com");
        result.Value.FirstName.Should().BeNull();
        result.Value.LastName.Should().BeNull();
        result.Value.DisplayName.Should().Be("cagatay.kamit");
    }

    [Fact]
    public async Task Handle_Should_ReturnTenantInfo_When_TenantContextPresent()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        ArrangeUser(userId);
        ArrangeClaimNames(userId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);

        var tenant = new Tenant("Vetinity Klinik");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        _tenants
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await CreateHandler().Handle(new GetAccountSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TenantId.Should().Be(tenantId);
        result.Value.TenantName.Should().Be("Vetinity Klinik");
    }

    [Fact]
    public async Task Handle_Should_ReturnActiveClinicInfo_When_ClinicContextPresent()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var clinicId = Guid.NewGuid();
        ArrangeUser(userId);
        ArrangeClaimNames(userId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns(clinicId);

        var tenant = new Tenant("Vetinity Klinik");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        _tenants
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var clinic = new Clinic(tenantId, "Merkez Şube", "İstanbul");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, clinicId);
        _clinics
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateHandler().Handle(new GetAccountSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActiveClinicId.Should().Be(clinicId);
        result.Value.ActiveClinicName.Should().Be("Merkez Şube");
    }

    [Fact]
    public async Task Handle_Should_ReturnNullClinicFields_When_ClinicContextMissing()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        ArrangeUser(userId);
        ArrangeClaimNames(userId);
        _tenantContext.SetupGet(t => t.TenantId).Returns(tenantId);
        _clinicContext.SetupGet(c => c.ClinicId).Returns((Guid?)null);

        var tenant = new Tenant("Vetinity Klinik");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, tenantId);
        _tenants
            .Setup(r => r.FirstOrDefaultAsync(It.IsAny<TenantByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);

        var result = await CreateHandler().Handle(new GetAccountSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ActiveClinicId.Should().BeNull();
        result.Value.ActiveClinicName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Should_SetIsTenantWideTrue_When_TenantWideClaimPresent()
    {
        var userId = Guid.NewGuid();
        ArrangeUser(userId);
        ArrangeClaimNames(userId, "Owner", "Veteriner");

        var result = await CreateHandler().Handle(new GetAccountSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsTenantWide.Should().BeTrue();
        result.Value.Roles.Should().Contain("Owner");
    }

    [Fact]
    public async Task Handle_Should_SetIsTenantWideFalse_When_NoTenantWideClaim()
    {
        var userId = Guid.NewGuid();
        ArrangeUser(userId);
        ArrangeClaimNames(userId, "ClinicAdmin", "Veteriner");

        var result = await CreateHandler().Handle(new GetAccountSummaryQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsTenantWide.Should().BeFalse();
    }

    [Fact]
    public void AccountSummaryDto_Should_NotExposeSensitiveFields()
    {
        var sensitiveNames = new[]
        {
            "PasswordHash",
            "RefreshToken",
            "SecurityStamp",
            "EmailConfirmed",
            "CreatedAtUtc",
            "UpdatedAtUtc",
            "Permissions",
        };

        var dtoProperties = typeof(AccountSummaryDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        dtoProperties.Should().NotContain(sensitiveNames);
    }
}
