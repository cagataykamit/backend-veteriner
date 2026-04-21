using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Reports.Appointments.Specs;

/// <summary>
/// <see cref="Appointment.ScheduledAtUtc"/> üzerinde kapalı aralık <c>[fromUtc, toUtc]</c> (UTC, dahil uçlar).
/// İstanbul takvim gününe çevrilmez — list endpoint’i ile aynı UTC penceresi semantiği.
/// </summary>
public sealed class AppointmentsReportFilteredCountSpec : Specification<Appointment>
{
    public AppointmentsReportFilteredCountSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        IReadOnlyList<Guid>? restrictedPetIdsForClient,
        AppointmentStatus? status,
        DateTime fromUtc,
        DateTime toUtc,
        string? searchContainsLikePattern,
        Guid[] searchPetIds)
    {
        Query.Where(a => a.TenantId == tenantId);
        if (clinicId.HasValue)
            Query.Where(a => a.ClinicId == clinicId.Value);
        if (petId.HasValue)
            Query.Where(a => a.PetId == petId.Value);
        if (restrictedPetIdsForClient is { Count: > 0 })
            Query.Where(a => restrictedPetIdsForClient.Contains(a.PetId));
        if (status.HasValue)
            Query.Where(a => a.Status == status.Value);

        Query.Where(a => a.ScheduledAtUtc >= fromUtc && a.ScheduledAtUtc <= toUtc);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(a =>
                (a.Notes != null && EF.Functions.Like(a.Notes, pat))
                || (pids.Length > 0 && pids.Contains(a.PetId)));
        }
    }
}
