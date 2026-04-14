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
    private readonly ILogger<GetDashboardSummaryQueryHandler> _logger;

    public GetDashboardSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IDashboardTodayAppointmentStatusCountsReader todayAppointmentCounts,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        ILogger<GetDashboardSummaryQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _todayAppointmentCounts = todayAppointmentCounts;
        _clients = clients;
        _pets = pets;
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
        var clientsTotal = await _clients.CountAsync(new DashboardClientsTotalCountSpec(tenantId), ct);
        MarkStep("clientsTotalCount");
        var petsTotal = await _pets.CountAsync(new DashboardPetsTotalCountSpec(tenantId), ct);
        MarkStep("petsTotalCount");

        // Dashboard listesi "bugun + gelecek" planlanmis randevulari gosterir; gecmis gun kayitlari disarida kalir.
        var upcomingRows = await _appointments.ListAsync(
            new DashboardUpcomingScheduledListSpec(tenantId, clinicId, dayStart, UpcomingListTake), ct);
        MarkStep("upcomingList");
        var recentClientRows = await _clients.ListAsync(
            new DashboardRecentClientsListSpec(tenantId, RecentListTake), ct);
        MarkStep("recentClientsList");
        var recentPetRows = await _pets.ListAsync(
            new DashboardRecentPetsListSpec(tenantId, RecentListTake), ct);
        MarkStep("recentPetsList");

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
                .ToList());

        _logger.LogInformation(
            "Dashboard summary generated. TenantId={TenantId} ClinicId={ClinicId} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            clinicId,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<DashboardSummaryDto>.Success(dto);
    }
}
