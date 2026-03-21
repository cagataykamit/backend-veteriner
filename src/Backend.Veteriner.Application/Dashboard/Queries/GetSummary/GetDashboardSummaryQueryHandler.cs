using Backend.Veteriner.Application.Common.Abstractions;
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
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;

    public GetDashboardSummaryQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Appointment> appointments,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets)
    {
        _tenantContext = tenantContext;
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
        var dayStart = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
        var dayEnd = dayStart.AddDays(1);

        var todayScheduledTask = _appointments.CountAsync(
            new DashboardTodayScheduledCountSpec(tenantId, dayStart, dayEnd), ct);
        var upcomingCountTask = _appointments.CountAsync(
            new DashboardUpcomingScheduledCountSpec(tenantId, utcNow), ct);
        var completedTodayTask = _appointments.CountAsync(
            new DashboardTodayCompletedCountSpec(tenantId, dayStart, dayEnd), ct);
        var cancelledTodayTask = _appointments.CountAsync(
            new DashboardTodayCancelledCountSpec(tenantId, dayStart, dayEnd), ct);
        var clientsTotalTask = _clients.CountAsync(new DashboardClientsTotalCountSpec(tenantId), ct);
        var petsTotalTask = _pets.CountAsync(new DashboardPetsTotalCountSpec(tenantId), ct);

        var upcomingListTask = _appointments.ListAsync(
            new DashboardUpcomingScheduledListSpec(tenantId, utcNow, UpcomingListTake), ct);
        var recentClientsTask = _clients.ListAsync(
            new DashboardRecentClientsListSpec(tenantId, RecentListTake), ct);
        var recentPetsTask = _pets.ListAsync(
            new DashboardRecentPetsListSpec(tenantId, RecentListTake), ct);

        await Task.WhenAll(
            todayScheduledTask,
            upcomingCountTask,
            completedTodayTask,
            cancelledTodayTask,
            clientsTotalTask,
            petsTotalTask,
            upcomingListTask,
            recentClientsTask,
            recentPetsTask);

        var upcomingRows = await upcomingListTask;
        var recentClientRows = await recentClientsTask;
        var recentPetRows = await recentPetsTask;

        var dto = new DashboardSummaryDto(
            TodayAppointmentsCount: await todayScheduledTask,
            UpcomingAppointmentsCount: await upcomingCountTask,
            CompletedTodayCount: await completedTodayTask,
            CancelledTodayCount: await cancelledTodayTask,
            TotalClientsCount: await clientsTotalTask,
            TotalPetsCount: await petsTotalTask,
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
                .Select(p => new DashboardRecentPetDto(p.Id, p.ClientId, p.Name, p.Species))
                .ToList());

        return Result<DashboardSummaryDto>.Success(dto);
    }
}
