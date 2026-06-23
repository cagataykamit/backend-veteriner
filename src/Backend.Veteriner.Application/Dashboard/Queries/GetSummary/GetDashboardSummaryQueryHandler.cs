using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetSummary;

public sealed class GetDashboardSummaryQueryHandler
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private const int UpcomingListTake = 20;
    private const int RecentListTake = 5;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IDashboardTodayAppointmentStatusCountsReader _todayAppointmentCounts;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IDashboardClinicScopedReader _clinicScopedReader;
    private readonly IDashboardAppointmentReadModelReader _dashboardAppointmentReader;
    private readonly QueryReadModelsOptions _queryReadModelsOptions;
    private readonly ILogger<GetDashboardSummaryQueryHandler> _logger;

    public GetDashboardSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Appointment> appointments,
        IDashboardTodayAppointmentStatusCountsReader todayAppointmentCounts,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IDashboardClinicScopedReader clinicScopedReader,
        IDashboardAppointmentReadModelReader dashboardAppointmentReader,
        IOptions<QueryReadModelsOptions> queryReadModelsOptions,
        ILogger<GetDashboardSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _appointments = appointments;
        _todayAppointmentCounts = todayAppointmentCounts;
        _clients = clients;
        _pets = pets;
        _clinicScopedReader = clinicScopedReader;
        _dashboardAppointmentReader = dashboardAppointmentReader;
        _queryReadModelsOptions = queryReadModelsOptions.Value;
        _logger = logger ?? NullLogger<GetDashboardSummaryQueryHandler>.Instance;
    }

    public async Task<Result<DashboardSummaryDto>> Handle(GetDashboardSummaryQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<DashboardSummaryDto>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var scopeResult = await _clinicScopeResolver.ResolveAsync(tenantId, _clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<DashboardSummaryDto>.Failure(scopeResult.Error);

        var singleClinicId = scopeResult.Value!.SingleClinicId;
        var accessibleClinicIds = scopeResult.Value.AccessibleClinicIds;

        var utcNow = DateTime.UtcNow;
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(utcNow);
        var trendBuckets = OperationPeriodBounds.Last7DaysForUtcNow(utcNow);

        if (accessibleClinicIds is { Count: 0 })
            return BuildEmptySummary(tenantId, singleClinicId, trendBuckets);

        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var timings = new DashboardSummaryStepTimings();

        void MarkStep(Action<long> recordElapsedMs)
        {
            recordElapsedMs(stepSw.ElapsedMilliseconds);
            stepSw.Restart();
        }

        if (_queryReadModelsOptions.DashboardAppointmentsEnabled)
        {
            return await HandleFromQueryReadModelAsync(
                tenantId,
                singleClinicId,
                accessibleClinicIds,
                utcNow,
                dayStart,
                dayEnd,
                trendBuckets,
                totalSw,
                timings,
                MarkStep,
                ct);
        }

        return await HandleFromCommandDbAsync(
            tenantId,
            singleClinicId,
            accessibleClinicIds,
            utcNow,
            dayStart,
            dayEnd,
            trendBuckets,
            totalSw,
            timings,
            MarkStep,
            ct);
    }

    private async Task<Result<DashboardSummaryDto>> HandleFromQueryReadModelAsync(
        Guid tenantId,
        Guid? singleClinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        DateTime utcNow,
        DateTime dayStart,
        DateTime dayEnd,
        IReadOnlyList<OperationPeriodBounds.DailyWindow> trendBuckets,
        Stopwatch totalSw,
        DashboardSummaryStepTimings timings,
        Action<Action<long>> markStep,
        CancellationToken ct)
    {
        var todayLocalDate = OperationDayBounds.ToLocalDate(utcNow);
        var readRequest = new DashboardAppointmentReadRequest(
            tenantId,
            singleClinicId,
            todayLocalDate,
            dayStart,
            dayEnd,
            utcNow,
            dayStart,
            UpcomingListTake,
            RecentListTake,
            trendBuckets,
            singleClinicId is null ? accessibleClinicIds : null);

        var appointmentData = await _dashboardAppointmentReader.GetAsync(readRequest, ct);
        markStep(ms => timings.TodayStatusCountsMs = ms);
        markStep(ms => timings.UpcomingAppointmentsCountMs = ms);
        markStep(ms => timings.UpcomingAppointmentsListMs = ms);
        markStep(ms => timings.Last7DaysAppointmentsMs = ms);

        int clientsTotal;
        int petsTotal;
        IReadOnlyList<DashboardRecentClientRow> recentClientRows;
        IReadOnlyList<DashboardRecentPetRow> recentPetRows;

        if (singleClinicId is not null || accessibleClinicIds is not null)
        {
            petsTotal = appointmentData.ClinicScopedPetsTotal ?? 0;
            clientsTotal = appointmentData.ClinicScopedClientsTotal ?? 0;
            recentPetRows = appointmentData.ClinicScopedRecentPets;
            recentClientRows = appointmentData.ClinicScopedRecentClients;
            markStep(ms => timings.TotalPetsMs = ms);
            markStep(ms => timings.TotalClientsMs = ms);
            markStep(ms => timings.RecentPetsMs = ms);
            markStep(ms => timings.RecentClientsMs = ms);
        }
        else
        {
            clientsTotal = await _clients.CountAsync(new DashboardClientsTotalCountSpec(tenantId), ct);
            markStep(ms => timings.TotalClientsMs = ms);
            petsTotal = await _pets.CountAsync(new DashboardPetsTotalCountSpec(tenantId), ct);
            markStep(ms => timings.TotalPetsMs = ms);
            recentClientRows = await _clients.ListAsync(
                new DashboardRecentClientsListSpec(tenantId, RecentListTake), ct);
            markStep(ms => timings.RecentClientsMs = ms);
            recentPetRows = await _pets.ListAsync(
                new DashboardRecentPetsListSpec(tenantId, RecentListTake), ct);
            markStep(ms => timings.RecentPetsMs = ms);
        }

        return BuildResult(
            tenantId,
            singleClinicId ?? _clinicContext.ClinicId,
            appointmentData.TodayCounts.Scheduled,
            appointmentData.UpcomingCount,
            appointmentData.TodayCounts.Completed,
            appointmentData.TodayCounts.Cancelled,
            clientsTotal,
            petsTotal,
            appointmentData.UpcomingAppointments,
            recentClientRows,
            recentPetRows,
            appointmentData.LastSevenDaysAppointments,
            totalSw,
            timings);
    }

    private async Task<Result<DashboardSummaryDto>> HandleFromCommandDbAsync(
        Guid tenantId,
        Guid? singleClinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        DateTime utcNow,
        DateTime dayStart,
        DateTime dayEnd,
        IReadOnlyList<OperationPeriodBounds.DailyWindow> trendBuckets,
        Stopwatch totalSw,
        DashboardSummaryStepTimings timings,
        Action<Action<long>> markStep,
        CancellationToken ct)
    {
        var trendStartUtc = trendBuckets[0].StartUtcInclusive;
        var trendEndUtc = trendBuckets[^1].EndUtcExclusive;

        var todayCounts = await _todayAppointmentCounts.GetAsync(
            tenantId, singleClinicId, dayStart, dayEnd, accessibleClinicIds, ct);
        markStep(ms => timings.TodayStatusCountsMs = ms);
        var upcomingCount = await _appointments.CountAsync(
            new DashboardUpcomingScheduledCountSpec(tenantId, singleClinicId, utcNow, accessibleClinicIds), ct);
        markStep(ms => timings.UpcomingAppointmentsCountMs = ms);

        int clientsTotal;
        int petsTotal;
        IReadOnlyList<DashboardRecentClientRow> recentClientRows;
        IReadOnlyList<DashboardRecentPetRow> recentPetRows;
        IReadOnlyList<DashboardUpcomingAppointmentRow> upcomingRows;
        IReadOnlyList<DashboardDailyCountDto> last7DaysAppointments;

        if (singleClinicId is { } clinicScope)
        {
            petsTotal = await _clinicScopedReader.CountPetsAtClinicAsync(tenantId, clinicScope, ct);
            markStep(ms => timings.TotalPetsMs = ms);
            clientsTotal = await _clinicScopedReader.CountClientsAtClinicAsync(tenantId, clinicScope, ct);
            markStep(ms => timings.TotalClientsMs = ms);

            upcomingRows = await _appointments.ListAsync(
                new DashboardUpcomingScheduledListSpec(tenantId, singleClinicId, dayStart, UpcomingListTake, accessibleClinicIds), ct);
            markStep(ms => timings.UpcomingAppointmentsListMs = ms);

            recentPetRows = await _clinicScopedReader.ListRecentPetsAtClinicAsync(
                tenantId, clinicScope, RecentListTake, ct);
            markStep(ms => timings.RecentPetsMs = ms);
            recentClientRows = await _clinicScopedReader.ListRecentClientsAtClinicAsync(
                tenantId, clinicScope, RecentListTake, ct);
            markStep(ms => timings.RecentClientsMs = ms);

            var trendScheduledAtUtcsClinic = await _appointments.ListAsync(
                new DashboardAppointmentScheduledAtInWindowSpec(
                    tenantId, singleClinicId, trendStartUtc, trendEndUtc, accessibleClinicIds), ct);
            markStep(ms => timings.Last7DaysAppointmentsMs = ms);
            last7DaysAppointments = BuildDailyCounts(trendBuckets, trendScheduledAtUtcsClinic);
        }
        else if (accessibleClinicIds is not null)
        {
            petsTotal = await _clinicScopedReader.CountPetsAtClinicsAsync(tenantId, accessibleClinicIds, ct);
            markStep(ms => timings.TotalPetsMs = ms);
            clientsTotal = await _clinicScopedReader.CountClientsAtClinicsAsync(tenantId, accessibleClinicIds, ct);
            markStep(ms => timings.TotalClientsMs = ms);

            upcomingRows = await _appointments.ListAsync(
                new DashboardUpcomingScheduledListSpec(tenantId, null, dayStart, UpcomingListTake, accessibleClinicIds), ct);
            markStep(ms => timings.UpcomingAppointmentsListMs = ms);

            recentPetRows = await _clinicScopedReader.ListRecentPetsAtClinicsAsync(
                tenantId, accessibleClinicIds, RecentListTake, ct);
            markStep(ms => timings.RecentPetsMs = ms);
            recentClientRows = await _clinicScopedReader.ListRecentClientsAtClinicsAsync(
                tenantId, accessibleClinicIds, RecentListTake, ct);
            markStep(ms => timings.RecentClientsMs = ms);

            var trendScheduledAtUtcsMulti = await _appointments.ListAsync(
                new DashboardAppointmentScheduledAtInWindowSpec(
                    tenantId, null, trendStartUtc, trendEndUtc, accessibleClinicIds), ct);
            markStep(ms => timings.Last7DaysAppointmentsMs = ms);
            last7DaysAppointments = BuildDailyCounts(trendBuckets, trendScheduledAtUtcsMulti);
        }
        else
        {
            clientsTotal = await _clients.CountAsync(new DashboardClientsTotalCountSpec(tenantId), ct);
            markStep(ms => timings.TotalClientsMs = ms);
            petsTotal = await _pets.CountAsync(new DashboardPetsTotalCountSpec(tenantId), ct);
            markStep(ms => timings.TotalPetsMs = ms);

            upcomingRows = await _appointments.ListAsync(
                new DashboardUpcomingScheduledListSpec(tenantId, null, dayStart, UpcomingListTake), ct);
            markStep(ms => timings.UpcomingAppointmentsListMs = ms);
            recentClientRows = await _clients.ListAsync(
                new DashboardRecentClientsListSpec(tenantId, RecentListTake), ct);
            markStep(ms => timings.RecentClientsMs = ms);
            recentPetRows = await _pets.ListAsync(
                new DashboardRecentPetsListSpec(tenantId, RecentListTake), ct);
            markStep(ms => timings.RecentPetsMs = ms);

            var trendScheduledAtUtcs = await _appointments.ListAsync(
                new DashboardAppointmentScheduledAtInWindowSpec(tenantId, null, trendStartUtc, trendEndUtc), ct);
            markStep(ms => timings.Last7DaysAppointmentsMs = ms);
            last7DaysAppointments = BuildDailyCounts(trendBuckets, trendScheduledAtUtcs);
        }

        return BuildResult(
            tenantId,
            singleClinicId ?? _clinicContext.ClinicId,
            todayCounts.Scheduled,
            upcomingCount,
            todayCounts.Completed,
            todayCounts.Cancelled,
            clientsTotal,
            petsTotal,
            upcomingRows,
            recentClientRows,
            recentPetRows,
            last7DaysAppointments,
            totalSw,
            timings);
    }

    private static Result<DashboardSummaryDto> BuildEmptySummary(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyList<OperationPeriodBounds.DailyWindow> trendBuckets)
    {
        var last7Days = trendBuckets
            .Select(b => new DashboardDailyCountDto(b.LocalDate, 0))
            .ToList();

        return Result<DashboardSummaryDto>.Success(
            new DashboardSummaryDto(
                TodayAppointmentsCount: 0,
                UpcomingAppointmentsCount: 0,
                CompletedTodayCount: 0,
                CancelledTodayCount: 0,
                TotalClientsCount: 0,
                TotalPetsCount: 0,
                UpcomingAppointments: [],
                RecentClients: [],
                RecentPets: [],
                Last7DaysAppointments: last7Days));
    }

    private static List<DashboardDailyCountDto> BuildDailyCounts(
        IReadOnlyList<OperationPeriodBounds.DailyWindow> buckets,
        IReadOnlyList<DateTime> timestampsUtc)
    {
        var counts = new int[buckets.Count];
        foreach (var ts in timestampsUtc)
        {
            for (var i = 0; i < buckets.Count; i++)
            {
                if (ts >= buckets[i].StartUtcInclusive && ts < buckets[i].EndUtcExclusive)
                {
                    counts[i]++;
                    break;
                }
            }
        }

        var result = new List<DashboardDailyCountDto>(buckets.Count);
        for (var i = 0; i < buckets.Count; i++)
            result.Add(new DashboardDailyCountDto(buckets[i].LocalDate, counts[i]));
        return result;
    }

    private Result<DashboardSummaryDto> BuildResult(
        Guid tenantId,
        Guid? clinicId,
        int todayScheduled,
        int upcomingCount,
        int completedToday,
        int cancelledToday,
        int clientsTotal,
        int petsTotal,
        IReadOnlyList<DashboardUpcomingAppointmentRow> upcomingRows,
        IReadOnlyList<DashboardRecentClientRow> recentClientRows,
        IReadOnlyList<DashboardRecentPetRow> recentPetRows,
        IReadOnlyList<DashboardDailyCountDto> last7DaysAppointments,
        Stopwatch totalSw,
        DashboardSummaryStepTimings timings)
    {
        var dto = new DashboardSummaryDto(
            TodayAppointmentsCount: todayScheduled,
            UpcomingAppointmentsCount: upcomingCount,
            CompletedTodayCount: completedToday,
            CancelledTodayCount: cancelledToday,
            TotalClientsCount: clientsTotal,
            TotalPetsCount: petsTotal,
            UpcomingAppointments: upcomingRows
                .Select(a => new DashboardAppointmentItemDto(
                    a.Id,
                    a.ClinicId,
                    a.PetId,
                    a.ScheduledAtUtc,
                    a.Status))
                .ToList(),
            RecentClients: recentClientRows
                .Select(c => new DashboardRecentClientDto(c.Id, c.FullName, c.Phone))
                .ToList(),
            RecentPets: recentPetRows
                .Select(p => new DashboardRecentPetDto(
                    p.Id,
                    p.ClientId,
                    p.Name,
                    p.SpeciesName))
                .ToList(),
            Last7DaysAppointments: last7DaysAppointments);

        var totalMs = totalSw.ElapsedMilliseconds;
        if (totalMs >= 250)
        {
            _logger.LogInformation(
                "DashboardSummaryTiming TenantId={TenantId} ClinicId={ClinicId} TotalMs={TotalMs} " +
                "TodayStatusCountsMs={TodayStatusCountsMs} UpcomingAppointmentsCountMs={UpcomingAppointmentsCountMs} " +
                "TotalClientsMs={TotalClientsMs} TotalPetsMs={TotalPetsMs} " +
                "UpcomingAppointmentsListMs={UpcomingAppointmentsListMs} RecentClientsMs={RecentClientsMs} " +
                "RecentPetsMs={RecentPetsMs} Last7DaysAppointmentsMs={Last7DaysAppointmentsMs}",
                tenantId,
                clinicId,
                totalMs,
                timings.TodayStatusCountsMs,
                timings.UpcomingAppointmentsCountMs,
                timings.TotalClientsMs,
                timings.TotalPetsMs,
                timings.UpcomingAppointmentsListMs,
                timings.RecentClientsMs,
                timings.RecentPetsMs,
                timings.Last7DaysAppointmentsMs);
        }

        return Result<DashboardSummaryDto>.Success(dto);
    }

    private sealed class DashboardSummaryStepTimings
    {
        public long TodayStatusCountsMs { get; set; }
        public long UpcomingAppointmentsCountMs { get; set; }
        public long TotalClientsMs { get; set; }
        public long TotalPetsMs { get; set; }
        public long UpcomingAppointmentsListMs { get; set; }
        public long RecentClientsMs { get; set; }
        public long RecentPetsMs { get; set; }
        public long Last7DaysAppointmentsMs { get; set; }
    }
}
