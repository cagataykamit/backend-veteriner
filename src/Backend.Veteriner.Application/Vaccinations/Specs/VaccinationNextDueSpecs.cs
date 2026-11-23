using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

/// <summary>
/// Sonraki uygulama tarihi olan uygulanmış aşılar (karşılanma denetiminin kaynakları, <see cref="VaccinationNextDueReads"/>). Verilen
/// süzgeçler birlikte uygulanır: vade aralığı <c>[dueFromUtc, dueToUtcExclusive)</c>, hayvanlar ve klinik kapsamı.
/// </summary>
public sealed class NextDueSourcesSpec : Specification<Vaccination, VaccinationDose>
{
    public NextDueSourcesSpec(
        Guid tenantId,
        Guid? clinicId,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        DateTime? dueFromUtc,
        DateTime? dueToUtcExclusive,
        IReadOnlyCollection<Guid>? petIds)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId
                         && v.Status == VaccinationStatus.Applied
                         && v.AppliedAtUtc != null
                         && v.DueAtUtc != null);
        if (dueFromUtc is { } from)
            Query.Where(v => v.DueAtUtc >= from);
        if (dueToUtcExclusive is { } to)
            Query.Where(v => v.DueAtUtc < to);
        if (petIds is not null)
        {
            var ids = petIds.ToArray();
            Query.Where(v => ids.Contains(v.PetId));
        }

        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Select(v => new VaccinationDose(v.Id, v.PetId, v.VaccineDefinitionId, v.VaccineName, v.AppliedAtUtc!.Value));
    }
}

/// <summary>
/// Hayvanların <paramref name="afterUtc"/>'den sonra uygulanmış ya da planlanmış aşıları (sonraki tarihin karşılanıp karşılanmadığı için,
/// <see cref="VaccinationDueRule.ClosesNextDueAfter"/>); kiracının bütün kliniklerinden (<see cref="VaccinationDueRule.IsNextDueFulfilled"/>).
/// </summary>
public sealed class NextDueClosingDosesForPetsSpec : Specification<Vaccination, VaccinationDose>
{
    public NextDueClosingDosesForPetsSpec(Guid tenantId, IReadOnlyCollection<Guid> petIds, DateTime afterUtc)
    {
        var ids = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && ids.Contains(v.PetId));
        Query.Where(VaccinationDueRule.ClosesNextDueAfter(afterUtc));
        Query.Select(VaccinationDueRule.AsClosingDose);
    }
}
