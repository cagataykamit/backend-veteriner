using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances.Specs;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LegalAcceptances.Commands.AcceptMy;

public sealed class AcceptMyLegalDocumentsCommandHandler
    : IRequestHandler<AcceptMyLegalDocumentsCommand, Result<MyLegalAcceptanceStatusDto>>
{
    private readonly IClientContext _client;
    private readonly ITenantContext _tenantContext;
    private readonly IUserOperationClaimRepository _userOperationClaims;
    private readonly IRepository<LegalAcceptance> _acceptances;
    private readonly TimeProvider _timeProvider;

    public AcceptMyLegalDocumentsCommandHandler(
        IClientContext client,
        ITenantContext tenantContext,
        IUserOperationClaimRepository userOperationClaims,
        IRepository<LegalAcceptance> acceptances,
        TimeProvider timeProvider)
    {
        _client = client;
        _tenantContext = tenantContext;
        _userOperationClaims = userOperationClaims;
        _acceptances = acceptances;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<MyLegalAcceptanceStatusDto>> Handle(AcceptMyLegalDocumentsCommand request, CancellationToken ct)
    {
        if (_client.UserId is not { } userId)
        {
            return Result<MyLegalAcceptanceStatusDto>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<MyLegalAcceptanceStatusDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var claimNames = await _userOperationClaims.GetOperationClaimNamesByUserIdAsync(userId, ct);
        var existing = await _acceptances.ListAsync(new LegalAcceptancesByUserSpec(userId), ct);
        var pending = LegalDocumentCatalog.Pending(LegalDocumentCatalog.RequiredFor(claimNames), existing);

        var check = LegalDocumentCatalog.EnsureAcceptsCurrent(pending, request.Documents);
        if (!check.IsSuccess)
            return Result<MyLegalAcceptanceStatusDto>.Failure(check.Error);

        if (pending.Count > 0)
        {
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
            await _acceptances.AddRangeAsync(
                LegalDocumentCatalog.CreateAcceptances(userId, tenantId, pending, utcNow), ct);
        }

        return Result<MyLegalAcceptanceStatusDto>.Success(
            new MyLegalAcceptanceStatusDto(false, Array.Empty<LegalDocumentVersionDto>()));
    }
}
