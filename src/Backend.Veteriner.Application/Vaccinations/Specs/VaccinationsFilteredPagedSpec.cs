using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Domain.Vaccinations;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

public sealed class VaccinationsFilteredPagedSpec : Specification<Vaccination>
{
    public VaccinationsFilteredPagedSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid? petId,
        VaccinationStatus? status,
        DateTime? dueFromUtc,
        DateTime? dueToUtc,
        DateTime? appliedFromUtc,
        DateTime? appliedToUtc,
        int page,
        int pageSize,
        string? searchContainsLikePattern,
        Guid[] searchPetIds,
        IReadOnlyCollection<Guid>? accessibleClinicIds = null,
        bool onlyOverdue = false,
        DateTime? listAsOfUtc = null,
        Guid? examinationId = null,
        IReadOnlyCollection<Guid>? notReceivingCarePetIds = null,
        IReadOnlyCollection<Guid>? clientPetIds = null,
        DateTime? dateFromUtc = null,
        DateTime? dateToUtc = null,
        ListSortChoice<VaccinationListSortField>? sort = null)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId);
        if (clinicId.HasValue)
        {
            Query.Where(v => v.ClinicId == clinicId.Value);
        }
        else if (accessibleClinicIds is not null)
        {
            if (accessibleClinicIds.Count == 0)
                Query.Where(v => false);
            else
                Query.Where(v => accessibleClinicIds.Contains(v.ClinicId));
        }
        if (petId.HasValue)
            Query.Where(v => v.PetId == petId.Value);
        if (examinationId.HasValue)
            Query.Where(v => v.ExaminationId == examinationId.Value);

        if (onlyOverdue)
            Query.Where(VaccinationDueRule.IsOverdue(VaccinationDueRule.OverdueBeforeUtc(listAsOfUtc ?? DateTime.UtcNow)));
        else if (status.HasValue)
        {
            Query.Where(v => v.Status == status.Value);
        }

        // Bakım dışı (vefat eden) hayvanların aşıları: gecikmiş liste ve onlyReceivingCare (ADR-020 S5; kimlikler Pet.ReceivesCare ile çözülür).
        if (notReceivingCarePetIds is { Count: > 0 })
            Query.Where(v => !notReceivingCarePetIds.Contains(v.PetId));

        if (dueFromUtc.HasValue)
            Query.Where(v => v.DueAtUtc != null && v.DueAtUtc >= dueFromUtc.Value);
        if (dueToUtc.HasValue)
            Query.Where(v => v.DueAtUtc != null && v.DueAtUtc <= dueToUtc.Value);
        if (appliedFromUtc.HasValue)
            Query.Where(v => v.AppliedAtUtc != null && v.AppliedAtUtc >= appliedFromUtc.Value);
        if (appliedToUtc.HasValue)
            Query.Where(v => v.AppliedAtUtc != null && v.AppliedAtUtc <= appliedToUtc.Value);
        VaccinationsListCriteria.WhereListDateInRange(Query, dateFromUtc, dateToUtc);
        VaccinationsListCriteria.WhereClientPetIds(Query, clientPetIds);

        if (searchContainsLikePattern is not null)
        {
            var pat = searchContainsLikePattern;
            var pids = searchPetIds;
            Query.Where(v =>
                EF.Functions.Like(v.VaccineName, pat)
                || (v.Notes != null && EF.Functions.Like(v.Notes, pat))
                || (pids.Length > 0 && pids.Contains(v.PetId)));
        }

        VaccinationsListCriteria.OrderBy(Query, sort)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);
    }
}
