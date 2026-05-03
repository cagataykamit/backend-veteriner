using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Queries.GetInviteById;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

public sealed class GetTenantInviteByIdQueryHandlerTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<TenantInvite>> _invites = new();
    private readonly Mock<IReadRepository<OperationClaim>> _claims = new();
    private readonly Mock<IReadRepository<Clinic>> _clinics = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();

    private GetTenantInviteByIdQueryHandler CreateHandler()
        => new(
            _tenantContext.Object,
            _permissions.Object,
            _invites.Object,
            _claims.Object,
            _clinics.Object,
            _userTenants.Object);

    private static TenantInvite BuildAcceptedInvite(Guid tid, Guid cid, Guid claimId, Guid userId)
    {
        var invite = TenantInvite.CreatePending(
            tid,
            cid,
            "x@y.com",
            new string('x', 64),
            claimId,
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(-1));
        invite.MarkAccepted(userId, DateTime.UtcNow);
        return invite;
    }

    [Fact]
    public async Task Accepted_AndStillMember_Should_IsCurrentMember_True()
    {
        var tid = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var uid = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildAcceptedInvite(tid, cid, claimId, uid);
        typeof(TenantInvite).GetProperty(nameof(TenantInvite.Id))!.SetValue(invite, inviteId);

        _invites.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var claim = new OperationClaim("Admin");
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(claim, claimId);
        _claims.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var clinic = new Clinic(tid, "K", "Ist");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        _userTenants.Setup(x => x.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateHandler().Handle(new GetTenantInviteByIdQuery(tid, inviteId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCurrentMember.Should().BeTrue();
    }

    [Fact]
    public async Task Accepted_ButRemoved_Should_IsCurrentMember_False()
    {
        var tid = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var uid = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildAcceptedInvite(tid, cid, claimId, uid);
        typeof(TenantInvite).GetProperty(nameof(TenantInvite.Id))!.SetValue(invite, inviteId);

        _invites.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        _claims.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationClaim?)null);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        _userTenants.Setup(x => x.ExistsAsync(uid, tid, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetTenantInviteByIdQuery(tid, inviteId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCurrentMember.Should().BeFalse();
    }

    [Fact]
    public async Task Pending_Should_IsCurrentMember_False()
    {
        var tid = Guid.NewGuid();
        var inviteId = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();

        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = TenantInvite.CreatePending(
            tid,
            cid,
            "p@y.com",
            new string('p', 64),
            claimId,
            DateTime.UtcNow.AddDays(7),
            DateTime.UtcNow.AddDays(-1));
        typeof(TenantInvite).GetProperty(nameof(TenantInvite.Id))!.SetValue(invite, inviteId);

        _invites.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        _claims.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationClaim?)null);
        _clinics.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Clinic?)null);

        var result = await CreateHandler().Handle(new GetTenantInviteByIdQuery(tid, inviteId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCurrentMember.Should().BeFalse();

        _userTenants.Verify(x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
