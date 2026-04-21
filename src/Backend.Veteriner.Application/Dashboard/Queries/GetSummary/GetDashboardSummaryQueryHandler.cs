using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetSummary;

public sealed class GetDashboardSummaryQueryHandler
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private const int UpcomingListTake = 20;
    private const int RecentListTake = 5;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IDashboardTodayAppointmentStatusCountsReader _todayAppointmentCounts;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IDashboardClinicScopedReader _clinicScopedReader;
    private readonly ILogger<GetDashboardSummaryQueryHandler> _logger;

    public GetDashboardSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IDashboardTodayAppointmentStatusCountsReader todayAppointmentCounts,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IDashboardClinicScopedReader clinicScopedReader,
        ILogger<GetDashboardSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _todayAppointmentCounts = todayAppointmentCounts;
        _clients = clients;
        _pets = pets;
        _clinicScopedReader = clinicScopedReader;
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

        var utcNow = DateTime.UtcNow;
        var (dayStart, dayEnd) = OperationDayBounds.ForUtcNow(utcNow);
        var trendBuckets = OperationPeriodBounds.Last7DaysForUtcNow(utcNow);
        var trendStartUtc = trendBuckets[0].StartUtcInclusive;
        var trendEndUtc = trendBuckets[^1].EndUtcExclusive;
        var clinicId = _clinicContext.ClinicId;
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        // Tek DbContext: paralel Task.WhenAll yerine sıralı await (EF Core concurrency kuralı).
        var todayCounts = await _todayAppointmentCounts.GetAsync(tenantId, clinicId, dayStart, dayEnd, ct);
        MarkStep("todayAppointmentStatusCounts");
        var todayScheduled = todayCounts.Scheduled;
        var completedToday = todayCounts.Completed;
        var cancelledToday = todayCounts.Cancelled;
        var upcomingCount = await _appointments.CountAsync(
            new DashboardUpcomingScheduledCountSpec(tenantId, clinicId, utcNow), ct);
        MarkStep("upcomingCount");
        // Aktif klinik varsa Client/Pet sayım ve listeleri klinik-dar (Appointment üzerinden);
        // yoksa mevcut tenant-geniş fallback korunur. DTO şekli değişmez (Faz 6A).
        int clientsTotal;
        int petsTotal;
        IReadOnlyList<DashboardRecentClientRow> recentClientRows;
        IReadOnlyList<DashboardRecentPetRow> recentPetRows;

        if (clinicId is { } clinicScope)
        {
            petsTotal = await _clinicScopedReader.CountPetsAtClinicAsync(tenantId, clinicScope, ct);
            MarkStep("petsTotalCount");
            clientsTotal = await _clinicScopedReader.CountClientsAtClinicAsync(tenantId, clinicScope, ct);
            MarkStep("clientsTotalCount");

            // Dashboard listesi "bugun + gelecek" planlanmis randevulari gosterir; gecmis gun kayitlari disarida kalir.
            var upcomingRowsClinic = await _appointments.ListAsync(
                new DashboardUpcomingScheduledListSpec(tenantId, clinicId, dayStart, UpcomingListTake), ct);
            MarkStep("upcomingList");

            recentPetRows = await _clinicScopedReader.ListRecentPetsAtClinicAsync(
                tenantId, clinicScope, RecentListTake, ct);
            MarkStep("recentPetsList");
            recentClientRows = await _clinicScopedReader.ListRecentClientsAtClinicAsync(
                tenantId, clinicScope, RecentListTake, ct);
            MarkStep("recentClientsList");

            var trendScheduledAtUtcsClinic = await _appointments.ListAsync(
                new DashboardAppointmentScheduledAtInWindowSpec(tenantId, clinicId, trendStartUtc, trendEndUtc), ct);
            MarkStep("last7DaysAppointments");
            var last7DaysAppointmentsClinic = BuildDailyCounts(trendBuckets, trendScheduledAtUtcsClinic);

            return BuildResult(
                tenantId,
                clinicId,
                todayScheduled,
                upcomingCount,
                completedToday,
                cancelledToday,
                clientsTotal,
                petsTotal,
                upcomingRowsClinic,
                recentClientRows,
                recentPetRows,
                last7DaysAppointmentsClinic,
                totalSw,
                querySteps,
                slowestStep,
                slowestMs);
        }

        clientsTotal = await _clients.CountAsync(new DashboardClientsTotalCountSpec(tenantId), ct);
        MarkStep("clientsTotalCount");
        petsTotal = await _pets.CountAsync(new DashboardPetsTotalCountSpec(tenantId), ct);
        MarkStep("petsTotalCount");

        // Dashboard listesi "bugun + gelecek" planlanmis randevulari gosterir; gecmis gun kayitlari disarida kalir.
        var upcomingRows = await _appointments.ListAsync(
            new DashboardUpcomingScheduledListSpec(tenantId, clinicId, dayStart, UpcomingListTake), ct);
        MarkStep("upcomingList");
        recentClientRows = await _clients.ListAsync(
            new DashboardRecentClientsListSpec(tenantId, RecentListTake), ct);
        MarkStep("recentClientsList");
        recentPetRows = await _pets.ListAsync(
            new DashboardRecentPetsListSpec(tenantId, RecentListTake), ct);
        MarkStep("recentPetsList");

        var trendScheduledAtUtcs = await _appointments.ListAsync(
            new DashboardAppointmentScheduledAtInWindowSpec(tenantId, clinicId, trendStartUtc, trendEndUtc), ct);
        MarkStep("last7DaysAppointments");
        var last7DaysAppointments = BuildDailyCounts(trendBuckets, trendScheduledAtUtcs);

        return BuildResult(
            tenantId,
            clinicId,
            todayScheduled,
            upcomingCount,
            completedToday,
            cancelledToday,
            clientsTotal,
            petsTotal,
            upcomingRows,
            recentClientRows,
            recentPetRows,
            last7DaysAppointments,
            totalSw,
            querySteps,
            slowestStep,
            slowestMs);
    }

    /// <summary>
    /// UTC timestamp'leri 7 günlük İstanbul bucket'larına [start, end) aralığı ile eşler; sonuç oldest→newest
    /// sıralı tam 7 eleman döner, boş günler 0 ile doldurulur.
    /// </summary>
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
        int querySteps,
        string slowestStep,
        long slowestMs)
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

        _logger.LogInformation(
            "Dashboard summary generated. TenantId={TenantId} ClinicId={ClinicId} ClinicScope={ClinicScope} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            clinicId,
            clinicId.HasValue ? "clinic" : "tenant",
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<DashboardSummaryDto>.Success(dto);
    }
}
