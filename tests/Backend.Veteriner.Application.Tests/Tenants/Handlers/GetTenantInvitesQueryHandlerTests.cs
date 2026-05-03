using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Tenants.Queries.GetInvites;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantInvitesQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<TenantInvite>> _invites = new();
    private readonly Mock<IReadRepository<OperationClaim>> _claims = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();

    private GetTenantInvitesQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _permissions.Object,
            _invites.Object,
            _claims.Object,
            _clinics.Object,
            _userTenants.Object);

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetTenantInvitesQuery(Guid.NewGuid(), new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetTenantInvitesQuery(Guid.NewGuid(), new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Handle_Should_ReturnFailure_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());
        var handler = CreateHandler();

        var result = await handler.Handle(
            new GetTenantInvitesQuery(Guid.NewGuid(), new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Handle_Should_ReturnEmptyPage_When_NoInvites()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _invites.Setup(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invites.Setup(x => x.ListAsync(It.IsAny<TenantInvitesPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite>());

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetTenantInvitesQuery(tid, new PageRequest { Page = 1, PageSize = 20 }, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task Handle_Should_Pass_Status_Filter_To_Specs()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _invites.Setup(x => x.CountAsync(It.Is<TenantInvitesCountSpec>(s => s.StatusFilter == TenantInviteStatus.Pending), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invites.Setup(x => x.ListAsync(It.Is<TenantInvitesPagedSpec>(s => s.StatusFilter == TenantInviteStatus.Pending), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite>());

        var handler = CreateHandler();
        await handler.Handle(
            new GetTenantInvitesQuery(tid, new PageRequest(), TenantInviteStatus.Pending),
            CancellationToken.None);

        _invites.Verify(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Pass_Search_To_Specs()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _invites.Setup(x => x.CountAsync(It.Is<TenantInvitesCountSpec>(s => s.SearchTermLower == "a@b.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invites.Setup(x => x.ListAsync(It.Is<TenantInvitesPagedSpec>(s => s.SearchTermLower == "a@b.com"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite>());

        var handler = CreateHandler();
        await handler.Handle(
            new GetTenantInvitesQuery(tid, new PageRequest { Search = "  A@B.COM  " }, null),
            CancellationToken.None);

        _invites.Verify(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Should_Set_IsExpired_For_Pending_Past_Expiry()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = TenantInvite.CreatePending(
            tid,
            cid,
            "a@b.com",
            new string('a', 64),
            claimId,
            DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(-2));

        _invites.Setup(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _invites.Setup(x => x.ListAsync(It.IsAny<TenantInvitesPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite> { invite });

        var claim = new OperationClaim("Veteriner");
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(claim, claimId);
        _claims.Setup(x => x.ListAsync(It.IsAny<OperationClaimsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OperationClaim> { claim });

        var clinic = new Clinic(tid, "K1", "Ist");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantAndIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic> { clinic });

        var handler = CreateHandler();
        var result = await handler.Handle(
            new GetTenantInvitesQuery(tid, new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].IsExpired.Should().BeTrue();
        result.Value.Items[0].IsCurrentMember.Should().BeFalse();
        result.Value.Items[0].ClinicName.Should().Be("K1");
        result.Value.Items[0].OperationClaimName.Should().Be("Veteriner");

        _userTenants.Verify(
            x => x.GetExistingUserIdsForTenantAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_Should_Set_IsCurrentMember_When_AcceptedAndStillMember()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = TenantInvite.CreatePending(
            tid,
            cid,
            "a@b.com",
            new string('b', 64),
            claimId,
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(-1));
        invite.MarkAccepted(uid, DateTime.UtcNow);

        _invites.Setup(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _invites.Setup(x => x.ListAsync(It.IsAny<TenantInvitesPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite> { invite });

        _claims.Setup(x => x.ListAsync(It.IsAny<OperationClaimsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OperationClaim>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantAndIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        _userTenants
            .Setup(x => x.GetExistingUserIdsForTenantAsync(tid, It.Is<IReadOnlyCollection<Guid>>(a => a.Contains(uid)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { uid });

        var result = await CreateHandler().Handle(
            new GetTenantInvitesQuery(tid, new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].IsCurrentMember.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_Clear_IsCurrentMember_When_AcceptedButRemovedFromTenant()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var uid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = TenantInvite.CreatePending(
            tid,
            cid,
            "a@b.com",
            new string('c', 64),
            claimId,
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(-1));
        invite.MarkAccepted(uid, DateTime.UtcNow);

        _invites.Setup(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _invites.Setup(x => x.ListAsync(It.IsAny<TenantInvitesPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite> { invite });

        _claims.Setup(x => x.ListAsync(It.IsAny<OperationClaimsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OperationClaim>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantAndIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        _userTenants
            .Setup(x => x.GetExistingUserIdsForTenantAsync(tid, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid>());

        var result = await CreateHandler().Handle(
            new GetTenantInvitesQuery(tid, new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].IsCurrentMember.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_Set_IsCurrentMember_False_For_Revoked()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = TenantInvite.CreatePending(
            tid,
            cid,
            "a@b.com",
            new string('d', 64),
            claimId,
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(-1));
        invite.Revoke();

        _invites.Setup(x => x.CountAsync(It.IsAny<TenantInvitesCountSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _invites.Setup(x => x.ListAsync(It.IsAny<TenantInvitesPagedSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantInvite> { invite });

        _claims.Setup(x => x.ListAsync(It.IsAny<OperationClaimsByIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OperationClaim>());
        _clinics.Setup(x => x.ListAsync(It.IsAny<ClinicsByTenantAndIdsSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Clinic>());

        var result = await CreateHandler().Handle(
            new GetTenantInvitesQuery(tid, new PageRequest(), null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items[0].IsCurrentMember.Should().BeFalse();

        _userTenants.Verify(
            x => x.GetExistingUserIdsForTenantAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
