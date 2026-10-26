using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalMe;

/// <summary>Oturumdaki portal hesabı ve kullanabildiği bağlar (aktif, aktif kiracıda).</summary>
public sealed record GetPortalMeQuery(Guid OwnerAccountId) : IRequest<Result<PortalMeDto>>;

public sealed class GetPortalMeQueryHandler : IRequestHandler<GetPortalMeQuery, Result<PortalMeDto>>
{
    private readonly OwnerPortalAccess _access;

    public GetPortalMeQueryHandler(OwnerPortalAccess access) => _access = access;

    public async ValueTask<Result<PortalMeDto>> Handle(GetPortalMeQuery request, CancellationToken ct)
    {
        var accountResult = await _access.GetActiveAccountAsync(request.OwnerAccountId, ct);
        if (!accountResult.IsSuccess)
            return Result<PortalMeDto>.Failure(accountResult.Error);
        var account = accountResult.Value!;

        var links = await _access.ListActiveLinksAsync(account.Id, ct);
        return Result<PortalMeDto>.Success(new PortalMeDto(
            account.Id,
            account.Email,
            account.DisplayName,
            account.HasPassword,
            links.Select(l => new PortalLinkSummaryDto(l.Link.Id, l.Link.ClinicId, l.ClinicName, l.ClientFullName)).ToArray()));
    }
}
