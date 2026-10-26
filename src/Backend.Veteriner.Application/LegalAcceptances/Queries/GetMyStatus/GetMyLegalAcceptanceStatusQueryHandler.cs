using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances.Specs;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LegalAcceptances.Queries.GetMyStatus;

public sealed class GetMyLegalAcceptanceStatusQueryHandler
    : IRequestHandler<GetMyLegalAcceptanceStatusQuery, Result<MyLegalAcceptanceStatusDto>>
{
    private readonly IClientContext _client;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IReadRepository<LegalAcceptance> _acceptances;

    public GetMyLegalAcceptanceStatusQueryHandler(
        IClientContext client,
        IUserOperationClaimRepository userOperationClaims,
        IReadRepository<LegalAcceptance> acceptances)
    {
        _client = client;
        _userOperationClaims = userOperationClaims;
        _acceptances = acceptances;
    }

    public async ValueTask<Result<MyLegalAcceptanceStatusDto>> Handle(GetMyLegalAcceptanceStatusQuery request, CancellationToken ct)
    {
        if (_client.UserId is not { } userId)
        {
            return Result<MyLegalAcceptanceStatusDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var claimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        var acceptances = await _acceptances.ListAsync(new LegalAcceptancesByUserSpec(userId), ct);
        var pending = LegalDocumentCatalog.Pending(LegalDocumentCatalog.RequiredFor(claimNames), acceptances);

        return Result<MyLegalAcceptanceStatusDto>.Success(
            new MyLegalAcceptanceStatusDto(pending.Count > 0, LegalDocumentCatalog.ToDtos(pending)));
    }
}
