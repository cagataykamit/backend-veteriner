using Backend.Veteriner.Application.Reports.Appointments;
using Backend.Veteriner.Application.Reports.Appointments.Contracts.Dtos;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Rapor filtreleriyle (durum hariç) status dağılımı; AppointmentsReportFilteredCountSpec ile aynı Where kümesi.
/// </summary>
public sealed class AppointmentsReportStatusBreakdownReader : IAppointmentsReportStatusBreakdownReader
{
    private readonly AppDbContext _db;

    public AppointmentsReportStatusBreakdownReader(AppDbContext db)
        => _db = db;

    public async Task<IReadOnlyList<AppointmentStatusCountRow>> GetAsync(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        IReadOnlyList<Guid>? restrictedPetIdsForClient,
        DateTime fromUtc,
        DateTime toUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        CancellationToken ct = default)
    {
        var q = _db.Appointments.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (clinicId.HasValue)
        {
            q = q.Where(a => a.ClinicId == clinicId.Value);
        }
        else if (accessibleClinicIds is not null)
        {
            if (accessibleClinicIds.Count == 0)
                q = q.Where(a => false);
            else
                q = q.Where(a => accessibleClinicIds.Contains(a.ClinicId));
        }
        if (petId.HasValue)
            q = q.Where(a => a.PetId == petId.Value);
        if (restrictedPetIdsForClient is { Count: > 0 })
            q = q.Where(a => restrictedPetIdsForClient.Contains(a.PetId));

        q = q.Where(a => a.ScheduledAtUtc >= fromUtc && a.ScheduledAtUtc <= toUtc);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            q = q.Where(a =>
                (a.Notes != null && EF.Functions.Like(a.Notes, pat))
                || (pids.Length > 0 && pids.Contains(a.PetId)));
        }

        var rows = await q
            .GroupBy(a => a.Status)
            .Select(g => new AppointmentStatusCountRow(g.Key, g.Count()))
            .ToListAsync(ct);

        return rows;
    }
}
