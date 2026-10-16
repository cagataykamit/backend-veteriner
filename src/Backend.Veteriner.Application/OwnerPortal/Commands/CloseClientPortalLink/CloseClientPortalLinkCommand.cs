using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.CloseClientPortalLink;

/// <summary>
/// Klinik paneli: hasta sahibinin bu müşteri kaydına portal erişimini kapatır (ADR-031 K3). Bağ silinmez; hesap ve başka
/// kliniklerdeki bağları açık kalır. Erişim her istekte bağdan çözüldüğü için etki anındadır. Abonelik salt okunurken de çalışır.
/// </summary>
public sealed record CloseClientPortalLinkCommand(Guid ClientId, Guid LinkId)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "OwnerPortal.Link.Close";
    public string? AuditTarget => $"ClientId={ClientId};LinkId={LinkId}";
}

public sealed class CloseClientPortalLinkCommandHandler : IRequestHandler<CloseClientPortalLinkCommand, Result>
{
    private readonly OwnerPortalStaffAccess _staffAccess;
    private readonly IRepository<OwnerAccountClientLink> _links;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public CloseClientPortalLinkCommandHandler(
        OwnerPortalStaffAccess staffAccess,
        IRepository<OwnerAccountClientLink> links,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _staffAccess = staffAccess;
        _links = links;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async Task<Result> Handle(CloseClientPortalLinkCommand request, CancellationToken ct)
    {
        var scopeResult = await _staffAccess.ResolveClientAsync(request.ClientId, ct);
        if (!scopeResult.IsSuccess)
            return Result.Failure(scopeResult.Error);
        var scope = scopeResult.Value!;

        var link = await _links.FirstOrDefaultAsync(
            new OwnerAccountClientLinkByClientAndIdSpec(scope.TenantId, scope.Client.Id, request.LinkId), ct);
        if (link is null)
            return Result.Failure("OwnerPortal.LinkNotFound", "Portal bağı bulunamadı.");

        var clinicAccess = await _staffAccess.EnsureClinicAssignedAsync(scope.UserId, link.ClinicId, ct);
        if (!clinicAccess.IsSuccess)
            return clinicAccess;

        var closed = link.Close(scope.UserId, _timeProvider.GetUtcNow().UtcDateTime);
        if (!closed.IsSuccess)
            return closed;

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
