using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.EDocuments.Contracts;
using Backend.Veteriner.Application.EDocuments.Providers;
using Backend.Veteriner.Application.EDocuments.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.EDocuments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Sales;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.EDocuments.Queries;

public sealed class GetEDocumentQueryHandler : IRequestHandler<GetEDocumentQuery, Result<EDocumentDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IReadRepository<EDocument> _documents;
    private readonly IReadRepository<Client> _clients;
    private readonly IEDocumentProviderResolver _providers;
    private readonly TimeProvider _timeProvider;

    public GetEDocumentQueryHandler(
        EDocumentAccess access,
        IReadRepository<EDocument> documents,
        IReadRepository<Client> clients,
        IEDocumentProviderResolver providers,
        TimeProvider timeProvider)
    {
        _access = access;
        _documents = documents;
        _clients = clients;
        _providers = providers;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<EDocumentDto>> Handle(GetEDocumentQuery request, CancellationToken ct)
    {
        var basics = _access.Basics(PermissionCatalog.EDocuments.Read);
        if (!basics.IsSuccess)
            return Result<EDocumentDto>.Failure(basics.Error);

        var document = await _documents.FirstOrDefaultAsync(new EDocumentByIdSpec(basics.Value!.TenantId, request.Id), ct);
        if (document is null || document.Status == EDocumentStatus.Draft)
            return Result<EDocumentDto>.Failure(EDocumentErrors.NotFound());

        var access = await _access.ResolveForClinicAsync(PermissionCatalog.EDocuments.Read, document.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentDto>.Failure(access.Error);

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(document.TenantId, document.ClientId), ct);
        return Result<EDocumentDto>.Success(document.ToDto(client?.FullName ?? string.Empty, _providers, _timeProvider.GetUtcNow().UtcDateTime));
    }
}

public sealed class GetEDocumentsListQueryHandler : IRequestHandler<GetEDocumentsListQuery, Result<PagedResult<EDocumentListItemDto>>>
{
    private readonly EDocumentAccess _access;
    private readonly IReadRepository<EDocument> _documents;
    private readonly IReadRepository<Client> _clients;

    public GetEDocumentsListQueryHandler(EDocumentAccess access, IReadRepository<EDocument> documents, IReadRepository<Client> clients)
    {
        _access = access;
        _documents = documents;
        _clients = clients;
    }

    public async ValueTask<Result<PagedResult<EDocumentListItemDto>>> Handle(GetEDocumentsListQuery request, CancellationToken ct)
    {
        var access = await _access.ResolveAsync(PermissionCatalog.EDocuments.Read, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<PagedResult<EDocumentListItemDto>>.Failure(access.Error);

        var caller = access.Value!;
        var total = await _documents.CountAsync(
            new EDocumentsFilteredSpec(caller.TenantId, caller.ClinicId, request.ClientId, request.Status, request.DocumentType, request.FromUtc, request.ToUtc), ct);
        var rows = await _documents.ListAsync(
            new EDocumentsFilteredPagedSpec(
                caller.TenantId, caller.ClinicId, request.ClientId, request.Status, request.DocumentType, request.FromUtc, request.ToUtc,
                request.Page, request.PageSize), ct);

        var names = await ClientNamesAsync(_clients, caller.TenantId, rows.Select(r => r.ClientId), ct);
        var items = rows.Select(r => r.ToListItem(names.GetValueOrDefault(r.ClientId, string.Empty))).ToList();
        return Result<PagedResult<EDocumentListItemDto>>.Success(
            PagedResult<EDocumentListItemDto>.Create(items, total, request.Page, request.PageSize));
    }

    internal static async Task<IReadOnlyDictionary<Guid, string>> ClientNamesAsync(
        IReadRepository<Client> clients, Guid tenantId, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length == 0)
            return new Dictionary<Guid, string>();
        return (await clients.ListAsync(new ClientsByTenantIdsSpec(tenantId, distinct), ct)).ToDictionary(c => c.Id, c => c.FullName);
    }
}

public sealed class GetEDocumentPdfQueryHandler : IRequestHandler<GetEDocumentPdfQuery, Result<EDocumentPdfDto>>
{
    private readonly EDocumentAccess _access;
    private readonly IReadRepository<EDocument> _documents;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly EDocumentProviderGateway _gateway;
    private readonly IUnitOfWork _uow;

    public GetEDocumentPdfQueryHandler(
        EDocumentAccess access,
        IReadRepository<EDocument> documents,
        IRepository<EDocumentConnection> connections,
        EDocumentProviderGateway gateway,
        IUnitOfWork uow)
    {
        _access = access;
        _documents = documents;
        _connections = connections;
        _gateway = gateway;
        _uow = uow;
    }

    public async ValueTask<Result<EDocumentPdfDto>> Handle(GetEDocumentPdfQuery request, CancellationToken ct)
    {
        var basics = _access.Basics(PermissionCatalog.EDocuments.Read);
        if (!basics.IsSuccess)
            return Result<EDocumentPdfDto>.Failure(basics.Error);

        var document = await _documents.FirstOrDefaultAsync(new EDocumentByIdSpec(basics.Value!.TenantId, request.Id), ct);
        if (document is null || document.Status == EDocumentStatus.Draft)
            return Result<EDocumentPdfDto>.Failure(EDocumentErrors.NotFound());

        var access = await _access.ResolveForClinicAsync(PermissionCatalog.EDocuments.Read, document.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentPdfDto>.Failure(access.Error);

        if (!EDocumentMappings.CanDownloadPdf(document))
            return Result<EDocumentPdfDto>.Failure("EDocuments.InvalidState", "Belge kesilmeden PDF alınamaz.");

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(document.TenantId, document.ClinicId, tracked: true), ct);
        var opened = _gateway.OpenFor(document, connection);
        if (!opened.IsSuccess)
            return Result<EDocumentPdfDto>.Failure(opened.Error);

        var session = opened.Value!;
        var pdf = await session.Provider.GetPdfAsync(session.Account, EDocumentSender.Refs(document), ct);
        _gateway.Absorb(connection!, pdf);
        await _uow.SaveChangesAsync(ct);

        if (!pdf.IsSuccess)
            return Result<EDocumentPdfDto>.Failure(EDocumentErrors.FromProvider(pdf.Error!));
        if (!pdf.Value!.IsReady)
            return Result<EDocumentPdfDto>.Failure("EDocuments.PdfNotReady", "Belgenin PDF'i henüz hazır değil; biraz sonra tekrar deneyin.");

        var fileName = $"{document.DocumentNumber ?? document.Id.ToString("N")}.pdf";
        return Result<EDocumentPdfDto>.Success(new EDocumentPdfDto(pdf.Value.Content!, fileName));
    }
}

public sealed class PreviewEDocumentQueryHandler : IRequestHandler<PreviewEDocumentQuery, Result<EDocumentPreviewDto>>
{
    private readonly EDocumentAccess _access;
    private readonly EDocumentDraftBuilder _drafts;
    private readonly EDocumentProviderGateway _gateway;
    private readonly IEDocumentProviderResolver _providers;
    private readonly IRepository<EDocumentConnection> _connections;
    private readonly IUnitOfWork _uow;

    public PreviewEDocumentQueryHandler(
        EDocumentAccess access,
        EDocumentDraftBuilder drafts,
        EDocumentProviderGateway gateway,
        IEDocumentProviderResolver providers,
        IRepository<EDocumentConnection> connections,
        IUnitOfWork uow)
    {
        _access = access;
        _drafts = drafts;
        _gateway = gateway;
        _providers = providers;
        _connections = connections;
        _uow = uow;
    }

    public async ValueTask<Result<EDocumentPreviewDto>> Handle(PreviewEDocumentQuery request, CancellationToken ct)
    {
        var access = await _access.ResolveAsync(PermissionCatalog.EDocuments.Read, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<EDocumentPreviewDto>.Failure(access.Error);

        var caller = access.Value!;
        var draftResult = await _drafts.BuildAsync(
            caller.TenantId, caller.ClinicId, new EDocumentSelection(request.ChargeIds, request.SaleId, request.VisitId), trackCharges: false, ct);
        if (!draftResult.IsSuccess)
            return Result<EDocumentPreviewDto>.Failure(draftResult.Error);
        var draft = draftResult.Value!;

        var connection = await _connections.FirstOrDefaultAsync(
            new EDocumentConnectionByClinicSpec(caller.TenantId, caller.ClinicId, tracked: true), ct);
        var opened = _gateway.Open(connection);

        // Kurumsal alıcının fatura grubu için mükellef sorgusu (bağlantı varsa); sorgu hatası önizlemeyi durdurmaz (e-Arşiv görünür).
        var isEInvoiceUser = false;
        if (opened.IsSuccess && draft.Buyer is { Kind: EDocumentBuyerKind.Corporate } buyer && draft.Groups.Any(g => !g.IsSelfEmployedReceipt))
        {
            var session = opened.Value!;
            var lookup = await session.Provider.FindEInvoiceRecipientAsync(session.Account, buyer.IdentityNumber, ct);
            _gateway.Absorb(connection!, lookup);
            isEInvoiceUser = lookup is { IsSuccess: true, Value: not null };
        }

        if (connection is not null)
            await _uow.SaveChangesAsync(ct);

        var groups = draft.Groups
            .Select(g => new EDocumentPreviewGroupDto(
                g.IsSelfEmployedReceipt ? EDocumentType.ESmm : isEInvoiceUser ? EDocumentType.EInvoice : EDocumentType.EArchive,
                g.ChargeIds,
                g.TotalAmount,
                g.Lines.Count))
            .ToList();

        var connectionDto = new EDocumentPreviewConnectionDto(
            opened.IsSuccess ? EDocumentConnectionStatus.Connected : connection?.Status ?? EDocumentConnectionStatus.NotConnected,
            connection?.Environment,
            connection is null ? null : _providers.Find(connection.ProviderCode)?.DisplayName);

        return Result<EDocumentPreviewDto>.Success(new EDocumentPreviewDto(
            draft.Client.Id,
            draft.Client.FullName,
            draft.Buyer is null ? null : EDocumentBuyerDto.From(draft.Buyer),
            draft.MissingBuyerFields,
            connectionDto,
            groups,
            draft.Skipped.Select(s => new EDocumentSkippedChargeDto(s.ChargeId, s.Reason)).ToList()));
    }
}

public sealed class GetUndocumentedChargesQueryHandler
    : IRequestHandler<GetUndocumentedChargesQuery, Result<PagedResult<UndocumentedChargeDto>>>
{
    private readonly EDocumentAccess _access;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Sale> _sales;
    private readonly IReadRepository<EDocument> _documents;

    public GetUndocumentedChargesQueryHandler(
        EDocumentAccess access,
        IReadRepository<Charge> charges,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IReadRepository<Sale> sales,
        IReadRepository<EDocument> documents)
    {
        _documents = documents;
        _access = access;
        _charges = charges;
        _clients = clients;
        _pets = pets;
        _sales = sales;
    }

    public async ValueTask<Result<PagedResult<UndocumentedChargeDto>>> Handle(GetUndocumentedChargesQuery request, CancellationToken ct)
    {
        var access = await _access.ResolveAsync(PermissionCatalog.EDocuments.Read, request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<PagedResult<UndocumentedChargeDto>>.Failure(access.Error);

        var caller = access.Value!;
        var failed = (await _documents.ListAsync(new FailedEDocumentsOfClinicSpec(caller.TenantId, caller.ClinicId), ct))
            .ToDictionary(f => f.Id, f => f.LastErrorMessage);
        var failedIds = failed.Keys.ToList();
        var total = await _charges.CountAsync(
            new UndocumentedChargesSpec(caller.TenantId, caller.ClinicId, request.ClientId, request.VisitId, request.FromUtc, request.ToUtc, failedIds), ct);
        var rows = await _charges.ListAsync(
            new UndocumentedChargesSpec(
                caller.TenantId, caller.ClinicId, request.ClientId, request.VisitId, request.FromUtc, request.ToUtc, failedIds, request.Page, request.PageSize), ct);

        var clientIds = rows.Select(r => r.ClientId).Distinct().ToArray();
        var clients = clientIds.Length == 0
            ? new Dictionary<Guid, Client>()
            : (await _clients.ListAsync(new ClientsByTenantIdsSpec(caller.TenantId, clientIds), ct)).ToDictionary(c => c.Id);
        var petIds = rows.Where(r => r.PetId.HasValue).Select(r => r.PetId!.Value).Distinct().ToArray();
        var petNames = petIds.Length == 0
            ? new Dictionary<Guid, string>()
            : (await _pets.ListAsync(new PetsByTenantIdsSpec(caller.TenantId, petIds), ct)).ToDictionary(p => p.Id, p => p.Name);
        var saleByCharge = rows.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await _sales.ListAsync(new SalesByChargeIdsSpec(caller.TenantId, rows.Select(r => r.Id).ToList()), ct))
                .ToDictionary(s => s.ChargeId!.Value, s => s.Id);

        var items = rows
            .Select(r => new UndocumentedChargeDto(
                r.Id,
                r.ClientId,
                clients.TryGetValue(r.ClientId, out var client) ? client.FullName : string.Empty,
                r.PetId,
                r.PetId is { } pid ? petNames.GetValueOrDefault(pid) : null,
                r.VisitId,
                saleByCharge.TryGetValue(r.Id, out var saleId) ? saleId : null,
                r.Description,
                r.Amount,
                r.OutstandingAmount,
                r.CreatedAtUtc,
                r.EDocumentId,
                UndocumentedReason(r, client, r.EDocumentId is { } docId ? failed.GetValueOrDefault(docId) : null)))
            .ToList();
        return Result<PagedResult<UndocumentedChargeDto>>.Success(
            PagedResult<UndocumentedChargeDto>.Create(items, total, request.Page, request.PageSize));
    }

    /// <summary>Kesilemeyen belgenin hatası, eksik alıcı kimliği veya bekleyen ödeme; hazırsa <c>null</c>.</summary>
    private static string? UndocumentedReason(Charge charge, Client? client, string? failedMessage)
    {
        if (charge.EDocumentId is not null)
            return string.IsNullOrWhiteSpace(failedMessage) ? "Belge kesilemedi." : $"Belge kesilemedi: {failedMessage}";
        if (client is not null && EDocumentDraftBuilder.BuildBuyer(client).Buyer is null)
            return "Müşterinin vergi numarası eksik.";
        if (charge.OutstandingAmount > 0m)
            return "Ödeme bekliyor.";
        return null;
    }
}
