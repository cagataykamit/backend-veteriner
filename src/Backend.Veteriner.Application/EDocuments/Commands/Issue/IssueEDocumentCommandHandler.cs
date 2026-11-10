using System.Security.Cryptography;
using System.Text;
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
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.EDocuments.Commands.Issue;

public sealed class IssueEDocumentCommandHandler : IRequestHandler<IssueEDocumentCommand, Result<EDocumentDto>>
{
    private readonly EDocumentAccess _access;
    private readonly EDocumentDraftBuilder _drafts;
    private readonly EDocumentProviderGateway _gateway;
    private readonly EDocumentSender _sender;
    private readonly IEDocumentProviderResolver _providers;
    private readonly ITrialLimitPolicy _trial;
    private readonly IRepository<EDocument> _documents;
    private readonly IReadRepository<EDocument> _documentsRead;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IReadRepository<Client> _clients;
    private readonly IUnitOfWork _uow;
    private readonly TimeProvider _timeProvider;

    public IssueEDocumentCommandHandler(
        EDocumentAccess access,
        EDocumentDraftBuilder drafts,
        EDocumentProviderGateway gateway,
        EDocumentSender sender,
        IEDocumentProviderResolver providers,
        ITrialLimitPolicy trial,
        IRepository<EDocument> documents,
        IReadRepository<EDocument> documentsRead,
        IRepository<EDocumentConnection> connections,
        IReadRepository<Client> clients,
        IUnitOfWork uow,
        TimeProvider timeProvider)
    {
        _access = access;
        _drafts = drafts;
        _gateway = gateway;
        _sender = sender;
        _providers = providers;
        _trial = trial;
        _documents = documents;
        _documentsRead = documentsRead;
        _connections = connections;
        _clients = clients;
        _uow = uow;
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
        var hash = Fingerprint(caller.ClinicId, selection, request.Note);

        var duplicate = await _documentsRead.FirstOrDefaultAsync(new EDocumentByRequestIdSpec(caller.TenantId, request.RequestId), ct);
        if (duplicate is not null)
            return await ReplayAsync(duplicate, hash, ct);

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(caller.TenantId, caller.ClinicId, tracked: true), ct);
        var opened = _gateway.Open(connection);
        if (!opened.IsSuccess)
            return Result<EDocumentDto>.Failure(opened.Error);
        var session = opened.Value!;

        // Deneme kiracısında gerçek belge kesilmez (ADR-036 ilke 7); test ortamında belge gerçek değildir.
        if (session.Provider.Environment == EDocumentEnvironment.Live && await _trial.IsTrialAsync(caller.TenantId, ct))
        {
            return Result<EDocumentDto>.Failure(
                TrialLimits.EDocumentsNotAllowedCode,
                "Deneme sürecinde gerçek e-belge kesilemez; aboneliği başlatınca kesebilirsiniz.");
        }

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

        if (draft.Buyer is not { } buyer)
        {
            return Result<EDocumentDto>.Failure(new Error(
                "EDocuments.BuyerIdentityMissing",
                "Kurumsal alıcının vergi numarası eksik; müşterinin fatura bilgilerini tamamlayın.",
                new Dictionary<string, object?> { ["missingFields"] = draft.MissingBuyerFields }));
        }

        var group = draft.Groups[0];
        var typeResult = await ResolveTypeAsync(draft, group, buyer, session, connection!, ct);
        if (!typeResult.IsSuccess)
        {
            await _connections.SaveChangesAsync(ct);
            return Result<EDocumentDto>.Failure(typeResult.Error);
        }

        var utcNow = Now();
        var created = EDocument.Create(
            caller.TenantId,
            caller.ClinicId,
            draft.Client.Id,
            draft.SaleId,
            draft.VisitId ?? (draft.Charges.Select(c => c.VisitId).Distinct().Count() == 1 ? draft.Charges[0].VisitId : null),
            typeResult.Value,
            session.Provider.Environment,
            session.Provider.ProviderCode,
            request.RequestId,
            hash,
            request.Note,
            buyer,
            group.Lines,
            draft.WithholdingRate,
            caller.UserId,
            utcNow);
        if (!created.IsSuccess)
            return Result<EDocumentDto>.Failure(created.Error);
        var document = created.Value!;

        foreach (var charge in draft.Charges.Where(c => group.ChargeIds.Contains(c.Id)))
        {
            var attached = charge.AttachEDocument(document.Id);
            if (!attached.IsSuccess)
                return Result<EDocumentDto>.Failure(attached.Error);
        }

        document.BeginSending(utcNow);
        // Süreç gönderim sırasında düşerse arka plan işi belgeye bakar (aynı dış referansla, çift belge oluşmaz).
        document.AwaitProviderResult(null, utcNow.Add(EDocumentSender.InFlightGrace), utcNow);

        try
        {
            await _documents.AddAsync(document, ct);
            await _uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<EDocumentDto>.Failure(
                "EDocuments.ConcurrencyConflict",
                "Seçilen ücretler aynı anda başka bir işlemle değişti; sayfayı yenileyip tekrar deneyin.");
        }
        catch (DbUpdateException)
        {
            // Aynı requestId ile eşzamanlı istek: benzersiz indeks kaybedene çarptı; kazanan döner.
            var winner = await _documentsRead.FirstOrDefaultAsync(new EDocumentByRequestIdSpec(caller.TenantId, request.RequestId), ct);
            if (winner is null)
                throw;
            return await ReplayAsync(winner, hash, ct);
        }

        await _sender.SendAsync(document, connection!, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<EDocumentDto>.Success(document.ToDto(draft.Client.FullName, _providers, Now()));
    }

    /// <summary>Fatura grubunda kurumsal alıcı için mükellef sorgusu: e-Fatura mükellefiyse e-Fatura, değilse e-Arşiv.</summary>
    private async Task<Result<EDocumentType>> ResolveTypeAsync(
        EDocumentDraftSet draft,
        EDocumentDraftGroup group,
        EDocumentBuyer buyer,
        EDocumentProviderSession session,
        EDocumentConnection connection,
        CancellationToken ct)
    {
        if (group.IsSelfEmployedReceipt)
            return Result<EDocumentType>.Success(EDocumentType.ESmm);
        if (buyer.Kind != EDocumentBuyerKind.Corporate)
            return Result<EDocumentType>.Success(EDocumentType.EArchive);

        var lookup = await session.Provider.FindEInvoiceRecipientAsync(session.Account, buyer.IdentityNumber, ct);
        _gateway.Absorb(connection, lookup);
        if (!lookup.IsSuccess)
            return Result<EDocumentType>.Failure(EDocumentErrors.FromProvider(lookup.Error!));

        return Result<EDocumentType>.Success(EDocumentTypeRule.Resolve(draft.TaxpayerType, group.Lines[0].LineKind, lookup.Value is not null));
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

    /// <summary>Kaynak + not özeti; aynı requestId farklı içerikle gelirse ayırt eder.</summary>
    public static string Fingerprint(Guid clinicId, EDocumentSelection selection, string? note)
    {
        var builder = new StringBuilder();
        builder.Append(clinicId.ToString("N")).Append('|');
        foreach (var id in (selection.ChargeIds ?? []).Distinct().OrderBy(x => x))
            builder.Append(id.ToString("N")).Append(',');
        builder.Append('|').Append(selection.SaleId?.ToString("N")).Append('|').Append(selection.VisitId?.ToString("N"));
        builder.Append('|').Append(note?.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private DateTime Now() => _timeProvider.GetUtcNow().UtcDateTime;
}
