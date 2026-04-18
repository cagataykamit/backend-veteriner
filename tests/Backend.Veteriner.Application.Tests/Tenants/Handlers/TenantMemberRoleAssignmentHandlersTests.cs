using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.AssignMemberRole;
using Backend.Veteriner.Application.Tenants.Commands.RemoveMemberRole;
using Backend.Veteriner.Domain.Auth;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Faz 3B: tenant-scoped rol atama/çıkarma.
/// Ortak davranışlar: yetki / context / tenant match / member-in-tenant / claim notfound / whitelist.
/// Idempotency: zaten atanmışsa AlreadyAssigned=true; zaten yoksa AlreadyRemoved=true (her iki durumda da cache invalidate edilmez, repo yazmaz).
/// Self-protect: caller kendi üzerinden rol çıkaramaz (Invites.SelfRoleRemoveForbidden).
/// Başarılı yazımlar sonrası <see cref="IPermissionCacheInvalidator.InvalidateUser"/> tek çağrı ile tetiklenir.
/// </summary>
public sealed class TenantMemberRoleAssignmentHandlersTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IClientContext> _clientContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IUserTenantRepository> _userTenantRepo = new();
    private readonly Mock<IReadRepository<OperationClaim>> _claimsRead = new();
    private readonly Mock<IUserOperationClaimRepository> _userOperationClaims = new();
    private readonly Mock<IPermissionCacheInvalidator> _cache = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private AssignTenantMemberRoleCommandHandler CreateAssignHandler()
        => new(_tenantContext.Object, _permissions.Object, _userTenantRepo.Object,
               _claimsRead.Object, _userOperationClaims.Object, _cache.Object, _uow.Object);

    private RemoveTenantMemberRoleCommandHandler CreateRemoveHandler()
        => new(_tenantContext.Object, _clientContext.Object, _permissions.Object, _userTenantRepo.Object,
               _claimsRead.Object, _userOperationClaims.Object, _cache.Object, _uow.Object);

    private static OperationClaim BuildClaim(Guid id, string name)
    {
        var claim = new OperationClaim(name);
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(claim, id);
        return claim;
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
            new AssignTenantMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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
            new AssignTenantMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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
            new AssignTenantMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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
            new AssignTenantMemberRoleCommand(tid, mid, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Members.NotFound");
        _claimsRead.Verify(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Should_Return_ClaimNotFound_When_Claim_Missing()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationClaim?)null);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.OperationClaimNotFound");
    }

    [Fact]
    public async Task Assign_Should_Return_NotAssignable_When_Claim_OffWhitelist()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClaim(cid, "Admin.Diagnostics"));

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.OperationClaimNotAssignable");
        _userOperationClaims.Verify(x => x.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(x => x.InvalidateUser(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Assign_Should_Add_And_Invalidate_On_HappyPath()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClaim(cid, "Veteriner"));
        _userOperationClaims.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(mid);
        result.Value.OperationClaimId.Should().Be(cid);
        result.Value.OperationClaimName.Should().Be("Veteriner");
        result.Value.AlreadyAssigned.Should().BeFalse();

        _userOperationClaims.Verify(x => x.AddAsync(
            It.Is<UserOperationClaim>(e => e.UserId == mid && e.OperationClaimId == cid),
            It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(x => x.InvalidateUser(mid), Times.Once);
    }

    [Fact]
    public async Task Assign_Should_Be_Idempotent_When_Already_Assigned()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        SetupHappyBasics(tid, mid);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClaim(cid, "Sekreter"));
        _userOperationClaims.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateAssignHandler().Handle(
            new AssignTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyAssigned.Should().BeTrue();
        result.Value.OperationClaimName.Should().Be("Sekreter");

        _userOperationClaims.Verify(x => x.AddAsync(It.IsAny<UserOperationClaim>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(x => x.InvalidateUser(It.IsAny<Guid>()), Times.Never);
    }

    // -------------------------------------------------------------------
    // REMOVE
    // -------------------------------------------------------------------

    [Fact]
    public async Task Remove_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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
            new RemoveTenantMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
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
            new RemoveTenantMemberRoleCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Remove_Should_Block_Self_Role_Removal()
    {
        var tid = Guid.NewGuid();
        var callerId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(callerId);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberRoleCommand(tid, callerId, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.SelfRoleRemoveForbidden");

        _userTenantRepo.Verify(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _userOperationClaims.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(x => x.InvalidateUser(It.IsAny<Guid>()), Times.Never);
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
            new RemoveTenantMemberRoleCommand(tid, mid, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Members.NotFound");
    }

    [Fact]
    public async Task Remove_Should_Return_ClaimNotFound_When_Claim_Missing()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationClaim?)null);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberRoleCommand(tid, mid, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.OperationClaimNotFound");
    }

    [Fact]
    public async Task Remove_Should_Return_NotAssignable_When_Claim_OffWhitelist()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClaim(cid, "Admin.Diagnostics"));

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.OperationClaimNotAssignable");
        _userOperationClaims.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Remove_Should_Remove_And_Invalidate_On_HappyPath()
    {
        var tid = Guid.NewGuid();
        var mid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _clientContext.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        _userTenantRepo.Setup(x => x.ExistsAsync(mid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClaim(cid, "ClinicAdmin"));
        _userOperationClaims.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.UserId.Should().Be(mid);
        result.Value.OperationClaimId.Should().Be(cid);
        result.Value.AlreadyRemoved.Should().BeFalse();

        _userOperationClaims.Verify(x => x.RemoveAsync(mid, cid, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _cache.Verify(x => x.InvalidateUser(mid), Times.Once);
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
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildClaim(cid, "Admin"));
        _userOperationClaims.Setup(x => x.ExistsAsync(mid, cid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateRemoveHandler().Handle(
            new RemoveTenantMemberRoleCommand(tid, mid, cid), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyRemoved.Should().BeTrue();

        _userOperationClaims.Verify(x => x.RemoveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _cache.Verify(x => x.InvalidateUser(It.IsAny<Guid>()), Times.Never);
    }
}
