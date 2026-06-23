using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Dashboard;

public sealed class DashboardTodayAppointmentStatusCountsReader : IDashboardTodayAppointmentStatusCountsReader
{
    private readonly AppDbContext _db;

    public DashboardTodayAppointmentStatusCountsReader(AppDbContext db)
        => _db = db;

    public async Task<DashboardTodayAppointmentStatusCounts> GetAsync(
        Guid tenantId,
        Guid? clinicId,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        CancellationToken ct = default)
    {
        var query = _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId &&
                a.ScheduledAtUtc >= dayStartUtc &&
                a.ScheduledAtUtc < dayEndUtc);

        if (clinicId is { } cid && cid != Guid.Empty)
        {
            query = query.Where(a => a.ClinicId == cid);
        }
        else if (accessibleClinicIds is not null)
        {
            if (accessibleClinicIds.Count == 0)
                return new DashboardTodayAppointmentStatusCounts(0, 0, 0);

            query = query.Where(a => accessibleClinicIds.Contains(a.ClinicId));
        }

        var rows = await query
            .GroupBy(a => a.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync(ct);

        var scheduled = 0;
        var completed = 0;
        var cancelled = 0;

        foreach (var row in rows)
        {
            switch (row.Status)
            {
                case AppointmentStatus.Scheduled:
                    scheduled = row.Count;
                    break;

                case AppointmentStatus.Completed:
                    completed = row.Count;
                    break;

                case AppointmentStatus.Cancelled:
                    cancelled = row.Count;
                    break;
            }
        }

        return new DashboardTodayAppointmentStatusCounts(
            scheduled,
            completed,
            cancelled);
    }
}
