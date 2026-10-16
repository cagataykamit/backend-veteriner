using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Queries.GetPortalLegalStatus;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Legal;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.OwnerPortal.Commands.AcceptPortalLegalDocuments;

/// <summary>
/// Portal hesabı bekleyen portal belgelerinin güncel sürümünü kabul eder (ADR-029 madde 5, personel ucuyla aynı kural):
/// her bekleyen belge için yeni satır (<c>OwnerAccountId</c>, kiracı yok); güncel olan/gerekmeyen belgeler yok sayılır.
/// </summary>
public sealed record AcceptPortalLegalDocumentsCommand(Guid OwnerAccountId, IReadOnlyList<LegalDocumentAcceptanceInput>? Documents)
    : IRequest<Result<PortalLegalStatusDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "Portal.LegalAcceptance";
    public string? AuditTarget => $"OwnerAccountId={OwnerAccountId}";
}

public sealed class AcceptPortalLegalDocumentsCommandHandler
    : IRequestHandler<AcceptPortalLegalDocumentsCommand, Result<PortalLegalStatusDto>>
{
    private readonly OwnerPortalAccess _access;
    private readonly IRepository<LegalAcceptance> _acceptances;
    private readonly TimeProvider _timeProvider;

    public AcceptPortalLegalDocumentsCommandHandler(
        OwnerPortalAccess access,
        IRepository<LegalAcceptance> acceptances,
        TimeProvider timeProvider)
    {
        _access = access;
        _acceptances = acceptances;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PortalLegalStatusDto>> Handle(AcceptPortalLegalDocumentsCommand request, CancellationToken ct)
    {
        var account = await _access.GetActiveAccountAsync(request.OwnerAccountId, ct);
        if (!account.IsSuccess)
            return Result<PortalLegalStatusDto>.Failure(account.Error);

        var pending = LegalDocumentCatalog.Pending(
            LegalDocumentCatalog.RequiredForOwnerAccount,
            await _acceptances.ListAsync(new LegalAcceptancesByOwnerAccountSpec(request.OwnerAccountId), ct));

        var check = LegalDocumentCatalog.EnsureAcceptsCurrent(pending, request.Documents);
        if (!check.IsSuccess)
            return Result<PortalLegalStatusDto>.Failure(check.Error);

        if (pending.Count > 0)
        {
            await _acceptances.AddRangeAsync(
                LegalDocumentCatalog.CreateOwnerAccountAcceptances(
                    request.OwnerAccountId, pending, _timeProvider.GetUtcNow().UtcDateTime), ct);
        }

        return Result<PortalLegalStatusDto>.Success(
            await GetPortalLegalStatusQueryHandler.BuildAsync(_access, _acceptances, request.OwnerAccountId, ct));
    }
}
