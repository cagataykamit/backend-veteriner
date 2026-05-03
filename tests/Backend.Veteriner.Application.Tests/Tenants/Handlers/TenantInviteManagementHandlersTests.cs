using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Auth.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Commands.CancelInvite;
using Backend.Veteriner.Application.Tenants.Commands.ResendInvite;
using Backend.Veteriner.Application.Tenants.Queries.GetInviteById;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Auth;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;
using FluentAssertions;
using Moq;

namespace Backend.Veteriner.Application.Tests.Tenants.Handlers;

/// <summary>
/// Faz 2 invite management testleri: detail / cancel / resend. Ortak davranışlar:
/// - Yetki eksik → Auth.PermissionDenied
/// - TenantContext yok → Tenants.ContextMissing
/// - JWT tenant ≠ route tenant → Tenants.AccessDenied
/// - Invite yoksa → Invites.NotFound
/// Cancel idempotent (Revoked yeniden çağrılınca AlreadyCancelled=true, success).
/// Resend yalnız Pending üzerinde; token hash+expiry güncellenir, CreatedAtUtc korunur.
/// </summary>
public sealed class TenantInviteManagementHandlersTests
{
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUserPermissionChecker> _permissions = new();
    private readonly Mock<IReadRepository<TenantInvite>> _invitesRead = new();
    private readonly Mock<IRepository<TenantInvite>> _invitesWrite = new();
    private readonly Mock<IReadRepository<OperationClaim>> _claimsRead = new();
    private readonly Mock<IReadRepository<Clinic>> _clinicsRead = new();
    private readonly Mock<IUserTenantRepository> _userTenants = new();
    private readonly Mock<ITokenHashService> _tokenHash = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private GetTenantInviteByIdQueryHandler CreateDetailHandler()
        => new(_tenantContext.Object, _permissions.Object, _invitesRead.Object, _claimsRead.Object, _clinicsRead.Object, _userTenants.Object);

    private CancelTenantInviteCommandHandler CreateCancelHandler()
        => new(_tenantContext.Object, _permissions.Object, _invitesRead.Object, _invitesWrite.Object, _uow.Object);

    private ResendTenantInviteCommandHandler CreateResendHandler()
        => new(_tenantContext.Object, _permissions.Object, _invitesRead.Object, _invitesWrite.Object, _tokenHash.Object, _uow.Object);

    private static TenantInvite BuildPending(Guid tenantId, Guid clinicId, Guid claimId, DateTime? expiresAtUtc = null, Guid? id = null)
    {
        var invite = TenantInvite.CreatePending(
            tenantId,
            clinicId,
            "a@b.com",
            new string('a', 64),
            claimId,
            expiresAtUtc ?? DateTime.UtcNow.AddDays(3),
            DateTime.UtcNow);
        if (id is { } explicitId)
            typeof(TenantInvite).GetProperty(nameof(TenantInvite.Id))!.SetValue(invite, explicitId);
        return invite;
    }

    // ============================================================
    //  DETAIL
    // ============================================================

    [Fact]
    public async Task Detail_Should_Fail_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateDetailHandler().Handle(
            new GetTenantInviteByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Detail_Should_Fail_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateDetailHandler().Handle(
            new GetTenantInviteByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Detail_Should_Fail_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateDetailHandler().Handle(
            new GetTenantInviteByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Detail_Should_Return_NotFound_When_Invite_Missing()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInvite?)null);

        var result = await CreateDetailHandler().Handle(
            new GetTenantInviteByIdQuery(tid, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.NotFound");
    }

    [Fact]
    public async Task Detail_Should_Return_Dto_With_IsExpired_And_Enrichment_SameTenantOnly()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, cid, claimId, expiresAtUtc: DateTime.UtcNow.AddDays(-1));
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(
                It.Is<TenantInviteByTenantAndIdSpec>(s => true), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var claim = new OperationClaim("Veteriner");
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(claim, claimId);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var clinic = new Clinic(tid, "K1", "Ist");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        var result = await CreateDetailHandler().Handle(
            new GetTenantInviteByIdQuery(tid, invite.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.TenantId.Should().Be(tid);
        dto.ClinicId.Should().Be(cid);
        dto.ClinicName.Should().Be("K1");
        dto.OperationClaimId.Should().Be(claimId);
        dto.OperationClaimName.Should().Be("Veteriner");
        dto.Status.Should().Be(TenantInviteStatus.Pending);
        dto.IsExpired.Should().BeTrue();
        dto.CanCancelInvite.Should().BeTrue();
        dto.CanResendInvite.Should().BeTrue();
        dto.IsCurrentMember.Should().BeFalse();

        _userTenants.Verify(
            x => x.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Detail_Should_Disable_LifecycleFlags_When_Accepted()
    {
        var tid = Guid.NewGuid();
        var cid = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var acceptedUserId = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, cid, claimId, expiresAtUtc: DateTime.UtcNow.AddDays(1));
        invite.MarkAccepted(acceptedUserId, DateTime.UtcNow);

        _invitesRead.Setup(x => x.FirstOrDefaultAsync(
                It.Is<TenantInviteByTenantAndIdSpec>(s => true), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var claim = new OperationClaim("Veteriner");
        typeof(OperationClaim).GetProperty(nameof(OperationClaim.Id))!.SetValue(claim, claimId);
        _claimsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<OperationClaimByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(claim);

        var clinic = new Clinic(tid, "K1", "Ist");
        typeof(Clinic).GetProperty(nameof(Clinic.Id))!.SetValue(clinic, cid);
        _clinicsRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<ClinicByIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic);

        _userTenants.Setup(x => x.ExistsAsync(acceptedUserId, tid, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateDetailHandler().Handle(
            new GetTenantInviteByIdQuery(tid, invite.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CanCancelInvite.Should().BeFalse();
        result.Value.CanResendInvite.Should().BeFalse();
        result.Value.IsCurrentMember.Should().BeTrue();
    }

    // ============================================================
    //  CANCEL
    // ============================================================

    [Fact]
    public async Task Cancel_Should_Fail_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Cancel_Should_Fail_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Cancel_Should_Fail_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Cancel_Should_Return_NotFound_When_Invite_Missing()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInvite?)null);

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(tid, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.NotFound");
    }

    [Fact]
    public async Task Cancel_HappyPath_Should_Mark_Revoked_And_Save()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid());
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(tid, invite.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyCancelled.Should().BeFalse();
        result.Value.Status.Should().Be(TenantInviteStatus.Revoked);
        invite.Status.Should().Be(TenantInviteStatus.Revoked);
        _invitesWrite.Verify(x => x.UpdateAsync(invite, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_Should_Be_Idempotent_When_Already_Revoked()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid());
        invite.Revoke();
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(tid, invite.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AlreadyCancelled.Should().BeTrue();
        result.Value.Status.Should().Be(TenantInviteStatus.Revoked);
        _invitesWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantInvite>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_Fail_InvalidState_When_Accepted()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid());
        invite.MarkAccepted(Guid.NewGuid(), DateTime.UtcNow);
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateCancelHandler().Handle(
            new CancelTenantInviteCommand(tid, invite.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.InvalidState");
        _invitesWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantInvite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ============================================================
    //  RESEND
    // ============================================================

    [Fact]
    public async Task Resend_Should_Fail_When_PermissionDenied()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(false);

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Auth.PermissionDenied");
    }

    [Fact]
    public async Task Resend_Should_Fail_When_ContextMissing()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns((Guid?)null);

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.ContextMissing");
    }

    [Fact]
    public async Task Resend_Should_Fail_When_TenantMismatch()
    {
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(Guid.NewGuid());

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(Guid.NewGuid(), Guid.NewGuid(), null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Tenants.AccessDenied");
    }

    [Fact]
    public async Task Resend_Should_Return_NotFound_When_Invite_Missing()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantInvite?)null);

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(tid, Guid.NewGuid(), null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.NotFound");
    }

    [Fact]
    public async Task Resend_HappyPath_Should_Reissue_Token_And_Refresh_Expiry()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid(), expiresAtUtc: DateTime.UtcNow.AddDays(-2));
        var originalTokenHash = invite.TokenHash;
        var originalCreatedAt = invite.CreatedAtUtc;

        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);
        _tokenHash.Setup(x => x.ComputeSha256(It.IsAny<string>())).Returns("new-hash");

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(tid, invite.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.InviteId.Should().Be(invite.Id);
        dto.Token.Should().NotBeNullOrWhiteSpace();
        dto.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        invite.TokenHash.Should().Be("new-hash").And.NotBe(originalTokenHash);
        invite.CreatedAtUtc.Should().Be(originalCreatedAt);
        invite.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        _invitesWrite.Verify(x => x.UpdateAsync(invite, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resend_Should_Fail_InvalidState_When_Accepted()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid());
        invite.MarkAccepted(Guid.NewGuid(), DateTime.UtcNow);
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(tid, invite.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.InvalidState");
        _invitesWrite.Verify(x => x.UpdateAsync(It.IsAny<TenantInvite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resend_Should_Fail_InvalidState_When_Revoked()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid());
        invite.Revoke();
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(tid, invite.Id, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.InvalidState");
    }

    [Fact]
    public async Task Resend_Should_Fail_ExpiryInvalid_When_Past()
    {
        var tid = Guid.NewGuid();
        _permissions.Setup(x => x.HasPermission(PermissionCatalog.Tenants.InviteCreate)).Returns(true);
        _tenantContext.SetupGet(x => x.TenantId).Returns(tid);

        var invite = BuildPending(tid, Guid.NewGuid(), Guid.NewGuid());
        _invitesRead.Setup(x => x.FirstOrDefaultAsync(It.IsAny<TenantInviteByTenantAndIdSpec>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(invite);

        var result = await CreateResendHandler().Handle(
            new ResendTenantInviteCommand(tid, invite.Id, DateTime.UtcNow.AddMinutes(-5)), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Invites.ExpiryInvalid");
    }
}
