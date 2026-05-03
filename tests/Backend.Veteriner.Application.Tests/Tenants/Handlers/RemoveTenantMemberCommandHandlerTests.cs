using Ardalis.Specification;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Auditing;
using Backend.Veteriner.Application.Tenants.Commands.RemoveMember;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class RemoveTenantMemberCommandHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IUserTenantRepository> _userTenantRepo = new();
    private readonly Mock<IReadRepository<UserTenant>> _userTenantsRead = new();
    private readonly Mock<IUserClinicRepository> _userClinics = new();
    private readonly Mock<IReadRepository<OperationClaim>> _claimsRead = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IAuditLogWriter> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private RemoveTenantMemberCommandHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _clientContext.Object,
            _permissions.Object,
            _userTenantRepo.Object,
            _userTenantsRead.Object,
            _userClinics.Object,
            _claimsRead.Object,
            _userOperationClaims.Object,
            _cache.Object,
            _audit.Object,
            _uow.Object);

    private static OperationClaim BuildClaim(Guid id, string name)
    {
        var claim = new OperationClaim(name);
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(claim, id);
        return claim;
    }

    [Fact]
    public async Task Remove_Should_Succeed_When_Valid()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(callerId);
        _userTenantRepo.Setup(x => x.ExistsAsync(memberId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userTenantsRead
            .Setup(x => x.CountAsync(It.IsAny<ISpecification<UserTenant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _claimsRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationClaim?)null);
        _userClinics
            .Setup(x => x.ListAccessibleClinicsAsync(memberId, tenantId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Clinic>());
        _userOperationClaims
            .Setup(x => x.GetDetailsByUserIdAsync(memberId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Backend.Veteriner.Application.Auth.Contracts.Dtos.UserOperationClaimDetailDto>());
        _userTenantRepo
            .Setup(x => x.TryRemoveMembershipAsync(memberId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new RemoveTenantMemberCommand(tenantId, memberId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MemberUserId.Should().Be(memberId);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(
            x => x.WriteAsync(It.Is<AuditLogEntry>(e => e.Action == "Tenants.MemberRemoved"), It.IsAny<CancellationToken>()),
            Times.Once);
        _cache.Verify(x => x.InvalidateUser(memberId), Times.Once);
    }

    [Fact]
    public async Task Remove_Should_Fail_When_Self()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(userId);

        var result = await CreateHandler().Handle(new RemoveTenantMemberCommand(tenantId, userId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("TenantMembers.CannotRemoveSelf");
        _userTenantRepo.Verify(
            x => x.TryRemoveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Remove_Should_Fail_When_Member_Not_In_Tenant()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(memberId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new RemoveTenantMemberCommand(tenantId, memberId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("TenantMembers.NotFound");
    }

    [Fact]
    public async Task Remove_Should_Fail_When_Sole_Member()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(memberId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userTenantsRead
            .Setup(x => x.CountAsync(It.IsAny<ISpecification<UserTenant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await CreateHandler().Handle(new RemoveTenantMemberCommand(tenantId, memberId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("TenantMembers.CannotRemoveSoleMember");
    }

    [Fact]
    public async Task Remove_Should_Fail_When_Last_Admin()
    {
        var tenantId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var adminClaimId = Guid.NewGuid();
        var adminClaim = BuildClaim(adminClaimId, "admin");

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tenantId);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(memberId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _userTenantsRead
            .Setup(x => x.CountAsync(It.IsAny<ISpecification<UserTenant>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _claimsRead
            .Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByNameSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminClaim);
        _userTenantRepo
            .Setup(x => x.CountMembersHavingOperationClaimAsync(tenantId, adminClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _userOperationClaims
            .Setup(x => x.ExistsAsync(memberId, adminClaimId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateHandler().Handle(new RemoveTenantMemberCommand(tenantId, memberId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("TenantMembers.CannotRemoveLastAdmin");
        _userTenantRepo.Verify(
            x => x.TryRemoveMembershipAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
