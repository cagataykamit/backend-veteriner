using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.ChargeSuggestions.Contracts.Dtos;
using Backend.Veteriner.Application.ChargeSuggestions.Queries.GetList;
using Backend.Veteriner.Application.Charges.Contracts.Dtos;
using Backend.Veteriner.Application.Charges.Queries.GetList;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Queries.GetBalance;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations.Specs;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetById;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Visits.Queries.GetCheckoutSummary;

/// <summary>
/// "Hesabı kapat" paneli okuması (CHECKOUT-001, <c>docs/VISIT_CHECKOUT_API_CONTRACT.md</c>). Yeni hesap kuralı yoktur: geliş, bekleyen
/// kalemler, gelişin ücretleri ve müşteri bakiyesi mevcut sorgularından gelir; bu sorgu yalnız onları gelişin kliniğine göre birleştirir.
/// </summary>
public sealed record GetVisitCheckoutSummaryQuery(Guid VisitId) : IRequest<Result<VisitCheckoutSummaryDto>>;

public sealed class GetVisitCheckoutSummaryQueryHandler
    : IRequestHandler<GetVisitCheckoutSummaryQuery, Result<VisitCheckoutSummaryDto>>
{
    /// <summary>Liste uçlarının sayfa üst sınırı; tek gelişte aşılmaz.</summary>
    private const int MaxRows = 200;

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly ISender _sender;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Examination> _examinations;
    private readonly IVisitOpenItemsReader _openItems;

    public GetVisitCheckoutSummaryQueryHandler(
        ITenantContext tenantContext,
        ICurrentUserPermissionChecker permissions,
        ISender sender,
        IReadRepository<Pet> pets,
        IReadRepository<Client> clients,
        IReadRepository<Examination> examinations,
        IVisitOpenItemsReader openItems)
    {
        _tenantContext = tenantContext;
        _permissions = permissions;
        _sender = sender;
        _pets = pets;
        _clients = clients;
        _examinations = examinations;
        _openItems = openItems;
    }

    public async ValueTask<Result<VisitCheckoutSummaryDto>> Handle(GetVisitCheckoutSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<VisitCheckoutSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        // Geliş erişimi GET /visits/{id} ile aynı kural (başka klinik ve kapsam dışı: NotFound).
        var visitResult = await _sender.Send(new GetVisitByIdQuery(request.VisitId), ct);
        if (!visitResult.IsSuccess)
            return Result<VisitCheckoutSummaryDto>.Failure(visitResult.Error);
        var visit = visitResult.Value!;

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(tenantId, visit.PetId), ct);
        if (pet is null)
            return NotFound();

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(tenantId, pet.ClientId), ct);
        if (client is null)
            return NotFound();

        VisitCheckoutChargesDto? charges = null;
        if (_permissions.HasPermission(PermissionCatalog.Charges.Read))
        {
            var loaded = await LoadChargesAsync(tenantId, visit, client.Id, ct);
            if (!loaded.IsSuccess)
                return Result<VisitCheckoutSummaryDto>.Failure(loaded.Error);
            charges = loaded.Value;
        }

        return Result<VisitCheckoutSummaryDto>.Success(new VisitCheckoutSummaryDto(
            visit.Id,
            visit.ClinicId,
            visit.CareStatus,
            visit.ArrivedAtUtc,
            visit.CompletedAtUtc,
            visit.IsVoided,
            visit.ResponsibleVeterinarianName,
            visit.ArrivalReason,
            pet.Id,
            pet.Name,
            pet.Species?.Name,
            client.Id,
            client.FullName,
            client.Phone,
            "TRY",
            charges));
    }

    private async Task<Result<VisitCheckoutChargesDto>> LoadChargesAsync(
        Guid tenantId, VisitDto visit, Guid clientId, CancellationToken ct)
    {
        // Ücret ve kalem gelişe doğrudan ya da muayenesi üzerinden bağlanır (açık iş göstergesiyle aynı bağ: VisitId ?? muayenenin VisitId'si).
        var examinationIds = (await _examinations.ListAsync(
                new ExaminationsByVisitIdsSpec(tenantId, [visit.Id], visit.ClinicId, null), ct))
            .Select(e => e.Id)
            .ToList();

        var pendingItems = new Dictionary<Guid, ChargeSuggestionDto>();
        var thisVisitCharges = new Dictionary<Guid, ChargeDto>();
        foreach (var examinationId in examinationIds.Cast<Guid?>().Prepend(null))
        {
            var pending = await _sender.Send(new GetChargeSuggestionsQuery(
                PageSize: MaxRows,
                ClinicId: visit.ClinicId,
                ExaminationId: examinationId,
                VisitId: examinationId is null ? visit.Id : null), ct);
            if (!pending.IsSuccess)
                return Result<VisitCheckoutChargesDto>.Failure(pending.Error);
            foreach (var s in pending.Value!.Items)
                pendingItems.TryAdd(s.Id, s);

            var charges = await _sender.Send(new GetChargesListQuery(
                PageSize: MaxRows,
                ClinicId: visit.ClinicId,
                VisitId: examinationId is null ? visit.Id : null,
                ExaminationId: examinationId), ct);
            if (!charges.IsSuccess)
                return Result<VisitCheckoutChargesDto>.Failure(charges.Error);
            foreach (var c in charges.Value!.Items)
                thisVisitCharges.TryAdd(c.Id, c);
        }

        var balance = await _sender.Send(new GetClientBalanceQuery(clientId), ct);
        if (!balance.IsSuccess)
            return Result<VisitCheckoutChargesDto>.Failure(balance.Error);

        var indicators = await _openItems.GetForVisitsAsync(
            OpenItemsScope.For(tenantId, visit.ClinicId, _permissions), [visit.Id], ct);

        return Result<VisitCheckoutChargesDto>.Success(new VisitCheckoutChargesDto(
            indicators.GetValueOrDefault(visit.Id)?.ChargeIndicator,
            pendingItems.Values
                .OrderBy(s => s.OccurredAtUtc).ThenBy(s => s.Id)
                .Select(s => new VisitCheckoutPendingItemDto(
                    s.Id, s.SourceType, s.PetId, s.PetName, s.Description, s.Quantity, s.Unit, s.SuggestedUnitPrice, s.OccurredAtUtc))
                .ToList(),
            thisVisitCharges.Values
                .OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
                .Select(c => new VisitCheckoutChargeDto(
                    c.Id, c.PetId, c.PetName, c.Description, c.Amount, c.AllocatedAmount, c.OutstandingAmount, c.CreatedAtUtc))
                .ToList(),
            OtherOpenCharges(balance.Value!, visit, thisVisitCharges.Keys.ToHashSet()),
            balance.Value!.CreditBalance));
    }

    /// <summary>
    /// Bakiye sorgusunun açık ücretlerinden gelişin kliniğindeki, bu gelişe ait olmayanlar (eskiden yeniye). Bakiye ücreti yalnız kendi
    /// <c>VisitId</c>'sine göre gruplar; gelişin ücret listesinde olan ücret (muayene üzerinden bağlı) burada tekrar gösterilmez.
    /// </summary>
    internal static IReadOnlyList<VisitCheckoutOtherChargeDto> OtherOpenCharges(
        ClientBalanceDto balance, VisitDto visit, IReadOnlySet<Guid> thisVisitChargeIds)
        => balance.Pets
            .SelectMany(p => p.Visits.SelectMany(v => v.Charges.Select(c => (Pet: p, Visit: v, Charge: c))))
            .Where(x => x.Charge.ClinicId == visit.ClinicId
                        && x.Visit.VisitId != visit.Id
                        && !thisVisitChargeIds.Contains(x.Charge.ChargeId)
                        && x.Charge.OutstandingAmount > 0)
            .OrderBy(x => x.Charge.CreatedAtUtc).ThenBy(x => x.Charge.ChargeId)
            .Select(x => new VisitCheckoutOtherChargeDto(
                x.Charge.ChargeId,
                x.Visit.VisitId,
                x.Visit.VisitArrivedAtUtc,
                x.Pet.PetId,
                x.Pet.PetName,
                x.Charge.Description,
                x.Charge.Amount,
                x.Charge.AllocatedAmount,
                x.Charge.OutstandingAmount,
                x.Charge.CreatedAtUtc))
            .ToList();

    private static Result<VisitCheckoutSummaryDto> NotFound()
        => Result<VisitCheckoutSummaryDto>.Failure("Visits.NotFound", "Geliş kaydı bulunamadı.");
}
