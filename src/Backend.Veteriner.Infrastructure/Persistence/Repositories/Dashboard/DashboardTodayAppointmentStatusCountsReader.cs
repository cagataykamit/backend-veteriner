using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Dashboard.Contracts;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
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
        CancellationToken ct = default)
    {
        var q = _db.Appointments.AsNoTracking()
            .Where(a =>
                a.TenantId == tenantId
                && a.ScheduledAtUtc >= dayStartUtc
                && a.ScheduledAtUtc < dayEndUtc);

        if (clinicId.HasValue)
            q = q.Where(a => a.ClinicId == clinicId.Value);

        // Tek tarama: durum bazlı koşullu sayım. Önce anonim tip (EF çevirir), sonra struct map (istemci).
        var row = await q
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Scheduled = g.Count(x => x.Status == AppointmentStatus.Scheduled),
                Completed = g.Count(x => x.Status == AppointmentStatus.Completed),
                Cancelled = g.Count(x => x.Status == AppointmentStatus.Cancelled)
            })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? default
            : new DashboardTodayAppointmentStatusCounts(row.Scheduled, row.Completed, row.Cancelled);
    }
}
