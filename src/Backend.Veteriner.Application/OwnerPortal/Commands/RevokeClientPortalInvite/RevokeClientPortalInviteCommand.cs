using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.RevokeClientPortalInvite;

/// <summary>Klinik paneli: bekleyen portal davetini iptal eder. Abonelik salt okunurken de çalışır (erişim daraltma güvenli taraf).</summary>
public sealed record RevokeClientPortalInviteCommand(Guid ClientId, Guid InviteId)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "OwnerPortal.Invite.Revoke";
    public string? AuditTarget => $"ClientId={ClientId};InviteId={InviteId}";
}

public sealed class RevokeClientPortalInviteCommandHandler : IRequestHandler<RevokeClientPortalInviteCommand, Result>
{
    private readonly OwnerPortalStaffAccess _staffAccess;
    private readonly IRepository<OwnerPortalInvite> _invites;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public RevokeClientPortalInviteCommandHandler(
        OwnerPortalStaffAccess staffAccess,
        IRepository<OwnerPortalInvite> invites,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _staffAccess = staffAccess;
        _invites = invites;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(RevokeClientPortalInviteCommand request, CancellationToken ct)
    {
        var scopeResult = await _staffAccess.ResolveClientAsync(request.ClientId, ct);
        if (!scopeResult.IsSuccess)
            return Result.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var invite = await _invites.FirstOrDefaultAsync(
            new OwnerPortalInviteByClientAndIdSpec(scope.TenantId, scope.Client.Id, request.InviteId), ct);
        if (invite is null)
            return Result.Failure("OwnerPortal.InviteNotFound", "Davet bulunamadı.");

        var clinicAccess = await _staffAccess.EnsureClinicAssignedAsync(scope.UserId, invite.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        var revoked = invite.Revoke(scope.UserId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!revoked.IsSuccess)
            return revoked;

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
