using Backend.Veteriner.Application.Charges.Specs;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.ReadModels;
using Backend.Veteriner.Domain.Charges;
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
    private readonly IVisitOpenItemsReader _openItems;
    private readonly IReadRepository<Charge> _charges;
    private readonly ICurrentUserPermissionChecker _permissions;
    private readonly IClinicVeterinarianReader _veterinarians;
    private readonly TimeProvider _timeProvider;

    public GetVisitsTodayQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IVisitTodayReader reader,
        IVisitOpenItemsReader openItems,
        IReadRepository<Charge> charges,
        ICurrentUserPermissionChecker permissions,
        IClinicVeterinarianReader veterinarians,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _reader = reader;
        _openItems = openItems;
        _charges = charges;
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

        var openScope = OpenItemsScope.For(tenantId, clinicId, _permissions);
        var items = await WithChargeAndOpenItemsAsync(openScope, data.Items, ct);

        var openItems = request.Voided
            ? null
            : await _openItems.GetSummaryAsync(BuildOpenItemsRequest(openScope, nowUtc, todayLocal), ct);

        return Result<TodayDto>.Success(new TodayDto(
            date,
            clinicId,
            nowUtc,
            request.Voided ? items.OrderByDescending(i => i.ArrivedAtUtc).ToList() : Order(items),
            data.ActiveHospitalizations,
            openItems));
    }

    /// <summary>Açık iş özeti isteği: ücretsiz ziyaret penceresi son 30 İstanbul günü, tamamlanmamış geliş = bugünden önceki açık geliş.</summary>
    private static OpenItemsSummaryRequest BuildOpenItemsRequest(OpenItemsScope scope, DateTime nowUtc, DateOnly todayLocal)
        => new(
            scope,
            OperationDayBounds.ForLocalDate(todayLocal.AddDays(-OpenItemsDefaults.UnchargedLookbackDays)).DayStartUtc,
            nowUtc,
            OperationDayBounds.ForLocalDate(todayLocal).DayStartUtc);

    /// <summary>
    /// Ücret tutarlarını ve açık iş alanlarını ekler: ziyaret bazında (hesap kuralı <see cref="IVisitOpenItemsReader"/>'da)
    /// ve müşterinin bu klinikteki toplam açık borcu. İzni olmayan türün alanları null kalır.
    /// </summary>
    private async Task<IReadOnlyList<TodayItemDto>> WithChargeAndOpenItemsAsync(
        OpenItemsScope scope, IReadOnlyList<TodayItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0)
            return items;

        IReadOnlyDictionary<Guid, VisitOpenItems> visitOpenItems = new Dictionary<Guid, VisitOpenItems>();
        var visitIds = items.Where(i => i.VisitId.HasValue).Select(i => i.VisitId!.Value).ToArray();
        if (visitIds.Length > 0 && scope.Visible != OpenItemSources.None)
            visitOpenItems = await _openItems.GetForVisitsAsync(scope, visitIds, ct);

        Dictionary<Guid, decimal>? clientOutstanding = null;
        if (scope.Sees(OpenItemSources.Charges))
        {
            var clientIds = items.Select(i => i.ClientId).Distinct().ToArray();
            var rows = await _charges.ListAsync(new ChargeAmountsForClientsSpec(scope.TenantId, scope.ClinicId, clientIds), ct);
            clientOutstanding = rows
                .GroupBy(r => r.ClientId)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount - r.AllocatedAmount));
        }

        return items
            .Select(i =>
            {
                var row = clientOutstanding is null
                    ? i
                    : i with { ClientOutstandingAmount = clientOutstanding.GetValueOrDefault(i.ClientId) };
                if (i.VisitId is not { } visitId || !visitOpenItems.TryGetValue(visitId, out var open))
                    return row;

                return row with
                {
                    ChargedAmount = open.ChargedAmount,
                    AllocatedAmount = open.AllocatedAmount,
                    OutstandingAmount = open.ChargedAmount - open.AllocatedAmount,
                    ChargeIndicator = open.ChargeIndicator,
                    ChargeableWorkCount = open.ChargeableWorkCount,
                    AwaitingLabCount = open.AwaitingLabCount,
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
