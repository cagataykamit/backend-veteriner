using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Public.Commands.OwnerSignup;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Application.Users.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using Backend.Veteriner.Domain.Users;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.Tests.Public.Handlers;

public sealed class PublicOwnerSignupCommandHandlerTests
{
    private readonly Mock<IReadRepository<User>> _usersRead = new();
    private readonly Mock<IUserRepository> _usersWrite = new();
    private readonly Mock<IReadRepository<Tenant>> _tenantsRead = new();
    private readonly Mock<IRepository<Tenant>> _tenantsWrite = new();
    private readonly Mock<IRepository<Clinic>> _clinicsWrite = new();
    private readonly Mock<IRepository<UserTenant>> _userTenantsWrite = new();
    private readonly Mock<IRepository<UserClinic>> _userClinicsWrite = new();
    private readonly Mock<IReadRepository<OperationClaim>> _operationClaimsRead = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IRepository<TenantSubscription>> _subscriptionsWrite = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILogger<PublicOwnerSignupCommandHandler>> _logger = new();

    private PublicOwnerSignupCommandHandler CreateHandler()
        => new(
            _usersRead.Object,
            _usersWrite.Object,
            _tenantsRead.Object,
            _tenantsWrite.Object,
            _clinicsWrite.Object,
            _userTenantsWrite.Object,
            _userClinicsWrite.Object,
            _operationClaimsRead.Object,
            _userOperationClaims.Object,
            _subscriptionsWrite.Object,
            _hasher.Object,
            _uow.Object,
            _logger.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PlanCodeInvalid()
    {
        var handler = CreateHandler();
        var command = new PublicOwnerSignupCommand("gold", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "12345678");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Subscriptions.PlanCodeInvalid");
        _usersRead.Verify(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_DuplicateEmail()
    {
        var handler = CreateHandler();
        var command = new PublicOwnerSignupCommand("Basic", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "12345678");

        _usersRead.Setup(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Users.DuplicateEmail");
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_Should_CreateOwnerTenantClinicMembershipsAndSubscription_When_Successful()
    {
        var handler = CreateHandler();
        var command = new PublicOwnerSignupCommand("Pro", "Tenant A", "Clinic A", "Istanbul", "owner@a.com", "12345678");
        var adminClaim = new OperationClaim("Admin");

        _usersRead.Setup(r => r.AnyAsync(It.IsAny<UserExistsByEmailSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _tenantsRead.Setup(r => r.AnyAsync(It.IsAny<TenantByNameCaseInsensitiveSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _operationClaimsRead.Setup(r => r.FirstOrDefaultAsync(It.IsAny<OperationClaimByNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminClaim);
        _hasher.Setup(h => h.Hash(command.Password)).Returns("hashed");

        TenantSubscription? capturedSub = null;
        _subscriptionsWrite.Setup(r => r.AddAsync(It.IsAny<TenantSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<TenantSubscription, CancellationToken>((s, _) => capturedSub = s);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;
        response.CanLogin.Should().BeTrue();
        response.NextStep.Should().Be("login");
        response.PlanCode.Should().Be("Pro");
        capturedSub.Should().NotBeNull();
        capturedSub!.PlanCode.Should().Be(SubscriptionPlanCode.Pro);
        capturedSub.Status.Should().Be(TenantSubscriptionStatus.Trialing);

        _usersWrite.Verify(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
        _tenantsWrite.Verify(r => r.AddAsync(It.IsAny<Tenant>(), It.IsAny<CancellationToken>()), Times.Once);
        _clinicsWrite.Verify(r => r.AddAsync(It.IsAny<Clinic>(), It.IsAny<CancellationToken>()), Times.Once);
        _userTenantsWrite.Verify(r => r.AddAsync(It.IsAny<UserTenant>(), It.IsAny<CancellationToken>()), Times.Once);
        _userClinicsWrite.Verify(r => r.AddAsync(It.IsAny<UserClinic>(), It.IsAny<CancellationToken>()), Times.Once);
        _userOperationClaims.Verify(r => r.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
