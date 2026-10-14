using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.ChargeSuggestions.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Visits;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Queries.GetToday;

public sealed class GetVisitsTodayQueryHandler : IRequestHandler<GetVisitsTodayQuery, Result<TodayDto>>
{
    /// <summary>Klinik/gün başına en çok satır; aşılırsa sessiz kesme yapılmaz, hata döner.</summary>
    public const int MaxItems = 500;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IVisitTodayReader _reader;
    private readonly IReadRepository<Charge> _charges;
    private readonly IReadRepository<ChargeSuggestion> _chargeSuggestions;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly TimeProvider _timeProvider;

    public GetVisitsTodayQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IVisitTodayReader reader,
        IReadRepository<Charge> charges,
        IReadRepository<ChargeSuggestion> chargeSuggestions,
        ICurrentUserPermissionChecker permissions,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
        _charges = charges;
        _chargeSuggestions = chargeSuggestions;
        _permissions = permissions;
        _veterinarians = veterinarians;
        _timeProvider = timeProvider;
    }

    public async Task<Result<TodayDto>> Handle(GetVisitsTodayQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<TodayDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (request.ClinicId.HasValue && _clinicContext.ClinicId.HasValue
            && request.ClinicId.Value != _clinicContext.ClinicId.Value)
        {
            return Result<TodayDto>.Failure(
                "Visits.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        if ((_clinicContext.ClinicId ?? request.ClinicId) is not { } clinicId)
        {
            return Result<TodayDto>.Failure(
                "Visits.ClinicScopeRequired",
                "Bugün görünümü için klinik belirlenemedi.");
        }

        var scope = await _clinicScopeResolver.ResolveAsync(tenantId, clinicId, ct);
        if (!scope.IsSuccess)
            return Result<TodayDto>.Failure(scope.Error);

        if (request.ResponsibleVeterinarianUserId is { } veterinarianId
            && !await _veterinarians.IsClinicVeterinarianAsync(veterinarianId, tenantId, clinicId, ct))
        {
            return Result<TodayDto>.Failure(
                "Visits.Validation",
                "Sorumlu hekim bu kliniğe atanmış aktif bir hekim (Veteriner) olmalıdır.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var todayLocal = OperationDayBounds.ToLocalDate(nowUtc);
        var date = request.LocalDate ?? todayLocal;
        var (dayStartUtc, dayEndUtc) = OperationDayBounds.ForLocalDate(date);

        var data = await _reader.GetAsync(
            new VisitTodayReadRequest(
                tenantId,
                clinicId,
                dayStartUtc,
                dayEndUtc,
                // Devralınan açık gelişler yalnızca bugünün görünümüne girer.
                IncludeCarriedOver: date == todayLocal,
                MaxItems,
                request.Voided,
                request.ResponsibleVeterinarianUserId),
            ct);

        if (data.Items.Count > MaxItems)
        {
            return Result<TodayDto>.Failure(
                "Visits.TodayLimitExceeded",
                $"Bugün görünümü en çok {MaxItems} satır gösterebilir.");
        }

        var items = await WithChargeAmountsAsync(tenantId, clinicId, data.Items, ct);

        return Result<TodayDto>.Success(new TodayDto(
            date,
            clinicId,
            nowUtc,
            request.Voided ? items.OrderByDescending(i => i.ArrivedAtUtc).ToList() : Order(items),
            data.ActiveHospitalizations));
    }

    /// <summary>
    /// Ücret tutarlarını ekler: ziyaret bazında, müşterinin bu klinikteki toplam açık borcu ve ziyaretin bekleyen ücret kalemi sayısı.
    /// <c>Charges.Read</c> yoksa satırlara dokunmaz (alanlar null).
    /// </summary>
    private async Task<IReadOnlyList<TodayItemDto>> WithChargeAmountsAsync(
        Guid tenantId, Guid clinicId, IReadOnlyList<TodayItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0 || !_permissions.HasPermission(PermissionCatalog.Charges.Read))
            return items;

        var clientIds = items.Select(i => i.ClientId).Distinct().ToArray();
        var rows = await _charges.ListAsync(new ChargeAmountsForClientsSpec(tenantId, clinicId, clientIds), ct);

        var clientOutstanding = rows
            .GroupBy(r => r.ClientId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount - r.AllocatedAmount));
        var visitTotals = rows
            .Where(r => r.VisitId.HasValue)
            .GroupBy(r => r.VisitId!.Value)
            .ToDictionary(g => g.Key, g => (Charged: g.Sum(r => r.Amount), Allocated: g.Sum(r => r.AllocatedAmount)));

        var visitIds = items.Where(i => i.VisitId.HasValue).Select(i => i.VisitId!.Value).Distinct().ToArray();
        var pendingByVisit = visitIds.Length == 0
            ? new Dictionary<Guid, int>()
            : (await _chargeSuggestions.ListAsync(new PendingChargeSuggestionVisitIdsSpec(tenantId, clinicId, visitIds), ct))
                .GroupBy(v => v)
                .ToDictionary(g => g.Key, g => g.Count());

        return items
            .Select(i =>
            {
                var withClient = i with { ClientOutstandingAmount = clientOutstanding.GetValueOrDefault(i.ClientId) };
                if (i.VisitId is not { } visitId)
                    return withClient;

                var (charged, allocated) = visitTotals.GetValueOrDefault(visitId);
                return withClient with
                {
                    ChargedAmount = charged,
                    AllocatedAmount = allocated,
                    OutstandingAmount = charged - allocated,
                    PendingChargeCount = pendingByVisit.GetValueOrDefault(visitId),
                };
            })
            .ToList();
    }

    /// <summary>
    /// Bekliyor (önce acil, geliş artan) → Devam ediyor (önce acil, geliş artan) → planlı (randevu artan) → Tamamlandı (geliş azalan) → Gelmedi (randevu artan).
    /// </summary>
    private static IReadOnlyList<TodayItemDto> Order(IReadOnlyList<TodayItemDto> items)
        => items
            .GroupBy(GroupRank)
            .OrderBy(g => g.Key)
            .SelectMany(OrderWithinGroup)
            .ToList();

    private static IEnumerable<TodayItemDto> OrderWithinGroup(IGrouping<int, TodayItemDto> group)
        => group.Key switch
        {
            WaitingRank or InProgressRank => group.OrderByDescending(i => i.IsUrgent).ThenBy(i => i.ArrivedAtUtc),
            PlannedRank or NoShowRank => group.OrderBy(i => i.ScheduledAtUtc),
            _ => group.OrderByDescending(i => i.ArrivedAtUtc),
        };

    private const int WaitingRank = 0;
    private const int InProgressRank = 1;
    private const int PlannedRank = 2;
    private const int CompletedRank = 3;
    private const int NoShowRank = 4;

    private static int GroupRank(TodayItemDto item)
        => item.CareStatus switch
        {
            VisitCareStatus.Waiting => WaitingRank,
            VisitCareStatus.InProgress => InProgressRank,
            VisitCareStatus.Completed => CompletedRank,
            _ => item.AppointmentStatus == AppointmentStatus.NoShow ? NoShowRank : PlannedRank,
        };
}
