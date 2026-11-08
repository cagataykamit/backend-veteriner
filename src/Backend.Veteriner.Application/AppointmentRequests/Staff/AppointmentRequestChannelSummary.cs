using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.AppointmentRequests.Staff;

/// <summary>
/// Kaynağa göre talep özeti (TIER-016): seçilen dönemde (İstanbul günleri, talep tarihi) her kaynaktan gelen talep ve bunlardan
/// onaylanan sayısı. Kapsam kuyrukla aynı (<see cref="StaffAppointmentRequestAccess"/>); <c>AppointmentRequests.Manage</c> gerekir.
/// </summary>
public sealed record GetAppointmentRequestChannelSummaryQuery(Guid? ClinicId, DateOnly From, DateOnly To)
    : IRequest<Result<AppointmentRequestChannelSummaryDto>>;

public sealed class GetAppointmentRequestChannelSummaryQueryHandler
    : IRequestHandler<GetAppointmentRequestChannelSummaryQuery, Result<AppointmentRequestChannelSummaryDto>>
{
    private readonly StaffAppointmentRequestAccess _access;
    private readonly IReadRepository<AppointmentRequest> _requests;

    public GetAppointmentRequestChannelSummaryQueryHandler(StaffAppointmentRequestAccess access, IReadRepository<AppointmentRequest> requests)
    {
        _access = access;
        _requests = requests;
    }

    public async ValueTask<Result<AppointmentRequestChannelSummaryDto>> Handle(GetAppointmentRequestChannelSummaryQuery request, CancellationToken ct)
    {
        if (RegisterReportFilter.RangeError(request.From, request.To) is { } rangeError)
            return Result<AppointmentRequestChannelSummaryDto>.Failure(rangeError);

        var scope = await _access.ResolveListScopeAsync(request.ClinicId, ct);
        if (!scope.IsSuccess)
            return Result<AppointmentRequestChannelSummaryDto>.Failure(scope.Error);
        var (tenantId, clinicIds) = scope.Value;

        var rows = await _requests.ListAsync(new AppointmentRequestChannelStatusSpec(
            tenantId,
            clinicIds,
            OperationDayBounds.ForLocalDate(request.From).DayStartUtc,
            OperationDayBounds.ForLocalDate(request.To).DayEndUtc), ct);

        // Kaynak sırasıyla, "bilinmiyor" (eski talepler) sonda; talep gelmeyen kaynak satırı yok.
        var items = rows
            .GroupBy(r => r.Channel)
            .OrderBy(g => g.Key is null)
            .ThenBy(g => g.Key)
            .Select(g => new AppointmentRequestChannelSummaryRowDto(
                g.Key, g.Count(), g.Count(r => r.Status == AppointmentRequestStatus.Approved)))
            .ToList();

        return Result<AppointmentRequestChannelSummaryDto>.Success(new AppointmentRequestChannelSummaryDto(
            request.From,
            request.To,
            items,
            items.Sum(i => i.Total),
            items.Sum(i => i.Approved)));
    }
}

public sealed record AppointmentRequestChannelStatusRow(AppointmentRequestChannel? Channel, AppointmentRequestStatus Status);

/// <summary>Dönemde oluşturulan talepler: yalnız kaynak ve durum (sayım bellekte).</summary>
public sealed class AppointmentRequestChannelStatusSpec : Specification<AppointmentRequest, AppointmentRequestChannelStatusRow>
{
    public AppointmentRequestChannelStatusSpec(Guid tenantId, IReadOnlyCollection<Guid>? clinicIds, DateTime fromUtc, DateTime toUtcExclusive)
    {
        Query.AsNoTracking().Where(x => x.TenantId == tenantId && x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtcExclusive);
        if (clinicIds is not null)
        {
            var ids = clinicIds.ToArray();
            Query.Where(x => ids.Contains(x.ClinicId));
        }

        Query.Select(x => new AppointmentRequestChannelStatusRow(x.Channel, x.Status));
    }
}
