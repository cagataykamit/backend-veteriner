using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Application.Dashboard.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard.ReadModels;
using Backend.Veteriner.Application.Dashboard.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Dashboard;

public sealed class DashboardAppointmentReadModelReader : IDashboardAppointmentReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public DashboardAppointmentReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<DashboardAppointmentReadResult> GetAsync(
        DashboardAppointmentReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var todayCounts = await GetTodayCountsAsync(request, cancellationToken);
        var upcomingCount = await GetUpcomingCountAsync(request, cancellationToken);
        var upcomingList = await GetUpcomingListAsync(request, cancellationToken);
        var lastSevenDays = await GetLastSevenDaysAsync(request, cancellationToken);

        int? petsTotal = null;
        int? clientsTotal = null;
        IReadOnlyList<DashboardRecentPetRow> recentPets = [];
        IReadOnlyList<DashboardRecentClientRow> recentClients = [];

        if (request.ClinicId is { } clinicId)
        {
            petsTotal = await _queryDb.ClinicPetActivityReadModels.AsNoTracking()
                .CountAsync(x => x.TenantId == request.TenantId && x.ClinicId == clinicId, cancellationToken);

            clientsTotal = await _queryDb.ClinicClientActivityReadModels.AsNoTracking()
                .CountAsync(x => x.TenantId == request.TenantId && x.ClinicId == clinicId, cancellationToken);

            recentPets = await _queryDb.ClinicPetActivityReadModels.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && x.ClinicId == clinicId)
                .OrderByDescending(x => x.LastAppointmentAtUtc)
                .ThenBy(x => x.PetId)
                .Take(request.RecentListLimit)
                .Select(x => new DashboardRecentPetRow(x.PetId, x.ClientId, x.PetName, x.SpeciesName))
                .ToListAsync(cancellationToken);

            recentClients = await _queryDb.ClinicClientActivityReadModels.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && x.ClinicId == clinicId)
                .OrderByDescending(x => x.LastAppointmentAtUtc)
                .ThenBy(x => x.ClientId)
                .Take(request.RecentListLimit)
                .Select(x => new DashboardRecentClientRow(x.ClientId, x.ClientName, x.ClientPhone))
                .ToListAsync(cancellationToken);
        }
        else if (request.AccessibleClinicIds is { Count: > 0 } accessibleClinicIds)
        {
            var clinicIds = accessibleClinicIds.ToArray();

            petsTotal = await _queryDb.ClinicPetActivityReadModels.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && clinicIds.Contains(x.ClinicId))
                .Select(x => x.PetId)
                .Distinct()
                .CountAsync(cancellationToken);

            clientsTotal = await _queryDb.ClinicClientActivityReadModels.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && clinicIds.Contains(x.ClinicId))
                .Select(x => x.ClientId)
                .Distinct()
                .CountAsync(cancellationToken);

            recentPets = await _queryDb.ClinicPetActivityReadModels.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && clinicIds.Contains(x.ClinicId))
                .OrderByDescending(x => x.LastAppointmentAtUtc)
                .ThenBy(x => x.PetId)
                .Take(request.RecentListLimit)
                .Select(x => new DashboardRecentPetRow(x.PetId, x.ClientId, x.PetName, x.SpeciesName))
                .ToListAsync(cancellationToken);

            recentClients = await _queryDb.ClinicClientActivityReadModels.AsNoTracking()
                .Where(x => x.TenantId == request.TenantId && clinicIds.Contains(x.ClinicId))
                .OrderByDescending(x => x.LastAppointmentAtUtc)
                .ThenBy(x => x.ClientId)
                .Take(request.RecentListLimit)
                .Select(x => new DashboardRecentClientRow(x.ClientId, x.ClientName, x.ClientPhone))
                .ToListAsync(cancellationToken);
        }

        return new DashboardAppointmentReadResult(
            todayCounts,
            upcomingCount,
            upcomingList,
            lastSevenDays,
            petsTotal,
            clientsTotal,
            recentPets,
            recentClients);
    }

    private async Task<DashboardTodayAppointmentStatusCounts> GetTodayCountsAsync(
        DashboardAppointmentReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AccessibleClinicIds is { Count: 0 })
            return new DashboardTodayAppointmentStatusCounts(0, 0, 0);

        var query = _queryDb.ClinicDailyAppointmentStatsReadModels.AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && x.LocalDate == request.TodayLocalDate);

        if (request.ClinicId is { } clinicId)
            query = query.Where(x => x.ClinicId == clinicId);
        else if (request.AccessibleClinicIds is { Count: > 0 } accessibleClinicIds)
            query = query.Where(x => accessibleClinicIds.Contains(x.ClinicId));

        var aggregated = await query
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Scheduled = g.Sum(x => x.ScheduledCount),
                Completed = g.Sum(x => x.CompletedCount),
                Cancelled = g.Sum(x => x.CancelledCount)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return aggregated is null
            ? new DashboardTodayAppointmentStatusCounts(0, 0, 0)
            : new DashboardTodayAppointmentStatusCounts(
                aggregated.Scheduled,
                aggregated.Completed,
                aggregated.Cancelled);
    }

    private async Task<int> GetUpcomingCountAsync(
        DashboardAppointmentReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AccessibleClinicIds is { Count: 0 })
            return 0;

        var query = _queryDb.AppointmentReadModels.AsNoTracking()
            .Where(x =>
                x.TenantId == request.TenantId
                && x.Status == (int)AppointmentStatus.Scheduled
                && x.ScheduledAtUtc > request.UpcomingCountFromUtcExclusive);

        if (request.ClinicId is { } clinicId)
            query = query.Where(x => x.ClinicId == clinicId);
        else if (request.AccessibleClinicIds is { Count: > 0 } accessibleClinicIds)
            query = query.Where(x => accessibleClinicIds.Contains(x.ClinicId));

        return await query.CountAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DashboardUpcomingAppointmentRow>> GetUpcomingListAsync(
        DashboardAppointmentReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AccessibleClinicIds is { Count: 0 })
            return [];

        var query = _queryDb.AppointmentReadModels.AsNoTracking()
            .Where(x =>
                x.TenantId == request.TenantId
                && x.Status == (int)AppointmentStatus.Scheduled
                && x.ScheduledAtUtc >= request.UpcomingListFromUtcInclusive);

        if (request.ClinicId is { } clinicId)
            query = query.Where(x => x.ClinicId == clinicId);
        else if (request.AccessibleClinicIds is { Count: > 0 } accessibleClinicIds)
            query = query.Where(x => accessibleClinicIds.Contains(x.ClinicId));

        return await query
            .OrderBy(x => x.ScheduledAtUtc)
            .ThenBy(x => x.AppointmentId)
            .Take(request.UpcomingListLimit)
            .Select(x => new DashboardUpcomingAppointmentRow(
                x.AppointmentId,
                x.ClinicId,
                x.PetId,
                x.ScheduledAtUtc,
                (AppointmentStatus)x.Status))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DashboardDailyCountDto>> GetLastSevenDaysAsync(
        DashboardAppointmentReadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AccessibleClinicIds is { Count: 0 })
        {
            return request.LastSevenDayBuckets
                .Select(b => new DashboardDailyCountDto(b.LocalDate, 0))
                .ToList();
        }

        var localDates = request.LastSevenDayBuckets.Select(b => b.LocalDate).ToArray();

        var query = _queryDb.ClinicDailyAppointmentStatsReadModels.AsNoTracking()
            .Where(x => x.TenantId == request.TenantId && localDates.Contains(x.LocalDate));

        if (request.ClinicId is { } clinicId)
            query = query.Where(x => x.ClinicId == clinicId);
        else if (request.AccessibleClinicIds is { Count: > 0 } accessibleClinicIds)
            query = query.Where(x => accessibleClinicIds.Contains(x.ClinicId));

        var totalsByDate = await query
            .GroupBy(x => x.LocalDate)
            .Select(g => new { LocalDate = g.Key, Total = g.Sum(x => x.TotalCount) })
            .ToListAsync(cancellationToken);

        var map = totalsByDate.ToDictionary(x => x.LocalDate, x => x.Total);

        return request.LastSevenDayBuckets
            .Select(b => new DashboardDailyCountDto(b.LocalDate, map.GetValueOrDefault(b.LocalDate, 0)))
            .ToList();
    }
}
