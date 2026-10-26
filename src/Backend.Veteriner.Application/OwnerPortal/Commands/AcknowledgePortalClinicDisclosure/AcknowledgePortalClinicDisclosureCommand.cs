using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.AcknowledgePortalClinicDisclosure;

/// <summary>Hasta sahibi bağın kliniğinin aydınlatma metnini okuduğunu onaylar (bilgilendirme kaydı; ilk onay zamanı kalır).</summary>
public sealed record AcknowledgePortalClinicDisclosureCommand(Guid OwnerAccountId, Guid LinkId)
    : IRequest<Result>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.ClinicDisclosure.Acknowledge";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId};LinkId={LinkId}";
}

public sealed class AcknowledgePortalClinicDisclosureCommandHandler : IRequestHandler<AcknowledgePortalClinicDisclosureCommand, Result>
{
    private readonly OwnerPortalAccess _access;
    private readonly IRepository<OwnerAccountClientLink> _links;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public AcknowledgePortalClinicDisclosureCommandHandler(
        OwnerPortalAccess access,
        IRepository<OwnerAccountClientLink> links,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _access = access;
        _links = links;
        _uow = uow;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result> Handle(AcknowledgePortalClinicDisclosureCommand request, CancellationToken ct)
    {
        var scope = await _access.ResolveLinkAsync(request.OwnerAccountId, request.LinkId, ct);
        if (!scope.IsSuccess)
            return Result.Failure(scope.Error);

        var link = await _links.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(scope.Value!.LinkId), ct);
        if (link is null)
            return Result.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");

        link.AcknowledgeDisclosure(_timeProvider.GetUtcNow().UtcDateTime);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
