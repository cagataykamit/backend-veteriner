using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Dashboard.Queries.GetSummary;

public sealed class GetDashboardSummaryQueryHandler
    : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private const int UpcomingListTake = 20;
    private const int RecentListTake = 5;

    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;

    public GetDashboardSummaryQueryHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _appointments = appointments;
        _clients = clients;
        _pets = pets;
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

        // Tek DbContext: paralel Task.WhenAll yerine sıralı await (EF Core concurrency kuralı).
        var todayScheduled = await _appointments.CountAsync(
            new DashboardTodayScheduledCountSpec(tenantId, clinicId, dayStart, dayEnd), ct);
        var upcomingCount = await _appointments.CountAsync(
            new DashboardUpcomingScheduledCountSpec(tenantId, clinicId, utcNow), ct);
        var completedToday = await _appointments.CountAsync(
            new DashboardTodayCompletedCountSpec(tenantId, clinicId, dayStart, dayEnd), ct);
        var cancelledToday = await _appointments.CountAsync(
            new DashboardTodayCancelledCountSpec(tenantId, clinicId, dayStart, dayEnd), ct);
        var clientsTotal = await _clients.CountAsync(new DashboardClientsTotalCountSpec(tenantId), ct);
        var petsTotal = await _pets.CountAsync(new DashboardPetsTotalCountSpec(tenantId), ct);

        // Dashboard listesi "bugun + gelecek" planlanmis randevulari gosterir; gecmis gun kayitlari disarida kalir.
        var upcomingRows = await _appointments.ListAsync(
            new DashboardUpcomingScheduledListSpec(tenantId, clinicId, dayStart, UpcomingListTake), ct);
        var recentClientRows = await _clients.ListAsync(
            new DashboardRecentClientsListSpec(tenantId, RecentListTake), ct);
        var recentPetRows = await _pets.ListAsync(
            new DashboardRecentPetsListSpec(tenantId, RecentListTake), ct);

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
                    p.Species?.Name ?? ""))
                .ToList());

        return Result<DashboardSummaryDto>.Success(dto);
    }
}
