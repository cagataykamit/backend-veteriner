using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalLegalStatus;

/// <summary>
/// Portal hesabının bekleyen kabulleri (ADR-029 mekanizması, portal belgeleri) ve aydınlatma metni onaylanmamış aktif bağlar
/// (ADR-031 K4). İstemci ikisi boşalana dek portalı engeller.
/// </summary>
public sealed record GetPortalLegalStatusQuery(Guid OwnerAccountId) : IRequest<Result<PortalLegalStatusDto>>;

public sealed class GetPortalLegalStatusQueryHandler : IRequestHandler<GetPortalLegalStatusQuery, Result<PortalLegalStatusDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IReadRepository<LegalAcceptance> _acceptances;

    public GetPortalLegalStatusQueryHandler(OwnerPortalAccess access, IReadRepository<LegalAcceptance> acceptances)
    {
        _access = access;
        _acceptances = acceptances;
    }

    public async ValueTask<Result<PortalLegalStatusDto>> Handle(GetPortalLegalStatusQuery request, CancellationToken ct)
    {
        var account = await _access.GetActiveAccountAsync(request.OwnerAccountId, ct);
        if (!account.IsSuccess)
            return Result<PortalLegalStatusDto>.Failure(account.Error);

        return Result<PortalLegalStatusDto>.Success(await BuildAsync(_access, _acceptances, request.OwnerAccountId, ct));
    }

    /// <summary>Durum hesaplaması; kabul komutu da yanıtını buradan üretir.</summary>
    internal static async Task<PortalLegalStatusDto> BuildAsync(
        OwnerPortalAccess access,
        IReadRepositoryBase<LegalAcceptance> acceptances,
        Guid ownerAccountId,
        CancellationToken ct)
    {
        var pending = LegalDocumentCatalog.Pending(
            LegalDocumentCatalog.RequiredForOwnerAccount,
            await acceptances.ListAsync(new LegalAcceptancesByOwnerAccountSpec(ownerAccountId), ct));

        var disclosures = (await access.ListActiveLinksAsync(ownerAccountId, ct))
            .Where(l => l.Link.DisclosureAcknowledgedAtUtc is null)
            .Select(l => new PortalPendingClinicDisclosureDto(l.Link.Id, l.Link.ClinicId, l.ClinicName))
            .ToArray();

        return new PortalLegalStatusDto(pending.Count > 0, LegalDocumentCatalog.ToDtos(pending), disclosures);
    }
}
