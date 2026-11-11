using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Commands.Issue;

public sealed class IssueEDocumentCommandHandler : IRequestHandler<IssueEDocumentCommand, Result<EDocumentDto>>
{
    private readonly EDocumentAccess _access;
    private readonly EDocumentDraftBuilder _drafts;
    private readonly EDocumentProviderGateway _gateway;
    private readonly EDocumentIssuer _issuer;
    private readonly IEDocumentProviderResolver _providers;
    private readonly ITrialLimitPolicy _trial;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IReadRepository<Client> _clients;
    private readonly TimeProvider _timeProvider;

    public IssueEDocumentCommandHandler(
        EDocumentAccess access,
        EDocumentDraftBuilder drafts,
        EDocumentProviderGateway gateway,
        EDocumentIssuer issuer,
        IEDocumentProviderResolver providers,
        ITrialLimitPolicy trial,
        IRepository<EDocumentConnection> connections,
        IReadRepository<Client> clients,
        TimeProvider timeProvider)
    {
        _access = access;
        _drafts = drafts;
        _gateway = gateway;
        _issuer = issuer;
        _providers = providers;
        _trial = trial;
        _connections = connections;
        _clients = clients;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentDto>> Handle(IssueEDocumentCommand request, CancellationToken ct)
    {
        var access = await _access.ResolveAsync(PermissionCatalog.EDocuments.Issue, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentDto>.Failure(access.Error);

        var caller = access.Value!;
        if (request.RequestId == Guid.Empty)
            return Result<EDocumentDto>.Failure("EDocuments.RequestIdRequired", "requestId zorunludur (çift istek koruması).");

        var selection = new EDocumentSelection(request.ChargeIds, request.SaleId, request.VisitId);
        var hash = EDocumentIssuer.Fingerprint(caller.ClinicId, selection, request.Note);

        var duplicate = await _issuer.FindByRequestIdAsync(caller.TenantId, request.RequestId, ct);
        if (duplicate is not null)
            return await ReplayAsync(duplicate, hash, ct);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(caller.TenantId, caller.ClinicId, tracked: true), ct);
        var opened = _gateway.Open(connection);
        if (!opened.IsSuccess)
            return Result<EDocumentDto>.Failure(opened.Error);
        var session = opened.Value!;

        var trialBlock = await EnsureNotTrialLiveAsync(_trial, session, caller.TenantId, ct);
        if (!trialBlock.IsSuccess)
            return Result<EDocumentDto>.Failure(trialBlock.Error);

        var draftResult = await _drafts.BuildAsync(caller.TenantId, caller.ClinicId, selection, trackCharges: true, ct);
        if (!draftResult.IsSuccess)
            return Result<EDocumentDto>.Failure(draftResult.Error);
        var draft = draftResult.Value!;

        if (draft.Skipped.FirstOrDefault() is { } skipped)
        {
            return skipped.Reason == EDocumentSkipReason.Voided
                ? Result<EDocumentDto>.Failure("EDocuments.ChargeVoided", "İptal edilmiş ücrete belge kesilemez.")
                : Result<EDocumentDto>.Failure("EDocuments.ChargeAlreadyDocumented", "Seçilen ücretlerden biri başka bir geçerli belgeye bağlı.");
        }

        if (draft.Groups.Count == 0)
            return Result<EDocumentDto>.Failure("EDocuments.NothingToDocument", "Belgesi kesilecek ücret yok.");
        if (draft.Groups.Count > 1)
        {
            return Result<EDocumentDto>.Failure(
                "EDocuments.MixedDocumentTypes",
                "Seçilen ücretler iki belge türüne ayrılıyor (e-SMM ve fatura); her grubu ayrı kesin.");
        }

        var issued = await _issuer.IssueGroupAsync(
            caller.TenantId, caller.ClinicId, caller.UserId, draft, draft.Groups[0], session, connection!,
            request.RequestId, hash, request.Note, EDocumentOrigin.Manual, ct);
        if (!issued.IsSuccess)
            return Result<EDocumentDto>.Failure(issued.Error);

        var document = issued.Value!;
        if (!string.Equals(document.RequestHash, hash, StringComparison.Ordinal))
            return await ReplayAsync(document, hash, ct);

        return Result<EDocumentDto>.Success(document.ToDto(draft.Client.FullName, _providers, Now()));
    }

    /// <summary>Deneme kiracısında gerçek belge kesilmez (ADR-036 ilke 7); test ortamında belge gerçek değildir.</summary>
    internal static async Task<Result> EnsureNotTrialLiveAsync(
        ITrialLimitPolicy trial, EDocumentProviderSession session, Guid tenantId, CancellationToken ct)
    {
        if (session.Provider.Environment == EDocumentEnvironment.Live && await trial.IsTrialAsync(tenantId, ct))
        {
            return Result.Failure(
                TrialLimits.EDocumentsNotAllowedCode,
                "Deneme sürecinde gerçek e-belge kesilemez; aboneliği başlatınca kesebilirsiniz.");
        }

        return Result.Success();
    }

    private async Task<Result<EDocumentDto>> ReplayAsync(EDocument existing, string hash, CancellationToken ct)
    {
        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
        {
            return Result<EDocumentDto>.Failure(
                "EDocuments.RequestIdConflict",
                "Bu requestId farklı içerikli bir belge isteğinde kullanılmış.");
        }

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(existing.TenantId, existing.ClientId), ct);
        return Result<EDocumentDto>.Success(existing.ToDto(client?.FullName ?? string.Empty, _providers, Now()));
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
