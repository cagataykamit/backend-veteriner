using Ardalis.Specification;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Domain.Visits;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>Aktif yatış (taburcu edilmemiş); history-summary'deki son 10 sınırından bağımsız.</summary>
public sealed class ActiveHospitalizationForPetSummarySpec : Specification<Hospitalization>
{
    public ActiveHospitalizationForPetSummarySpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(h => h.TenantId == tenantId && h.PetId == petId && h.DischargedAtUtc == null);
        DashboardSpecificationClinicScope.ApplyToHospitalization(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(h => h.AdmittedAtUtc)
            .ThenByDescending(h => h.Id)
            .Take(1)
            .AsNoTracking();
    }
}

/// <summary>En yeni uygulanmış aşı.</summary>
public sealed class LastAppliedVaccinationForPetSpec : Specification<Vaccination>
{
    public LastAppliedVaccinationForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(v => v.TenantId == tenantId
                         && v.PetId == petId
                         && v.Status == VaccinationStatus.Applied
                         && v.AppliedAtUtc != null);
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(v => v.AppliedAtUtc)
            .ThenByDescending(v => v.Id)
            .Take(1)
            .AsNoTracking();
    }
}

/// <summary>Planlı aşılar içinde en erken vadeli (gecikmiş olabilir).</summary>
public sealed class NextDueVaccinationForPetSpec : Specification<Vaccination>
{
    public NextDueVaccinationForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(v => v.TenantId == tenantId
                         && v.PetId == petId
                         && v.Status == VaccinationStatus.Scheduled
                         && v.DueAtUtc != null);
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
        Query.OrderBy(v => v.DueAtUtc)
            .ThenBy(v => v.Id)
            .Take(1)
            .AsNoTracking();
    }
}

/// <summary>Kilo ölçülmüş en yeni muayene (ölçüm zamanı, yoksa muayene zamanı).</summary>
public sealed class LatestWeightExaminationForPetSpec : Specification<Examination>
{
    public LatestWeightExaminationForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(e => e.TenantId == tenantId && e.PetId == petId && e.WeightKg != null);
        DashboardSpecificationClinicScope.ApplyToExamination(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(e => e.VitalsMeasuredAtUtc ?? e.ExaminedAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(1)
            .AsNoTracking();
    }
}

/// <summary>Yanlış işaretli olmayan en yeni geliş.</summary>
public sealed class LastVisitForPetSpec : Specification<Visit>
{
    public LastVisitForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(v => v.TenantId == tenantId && v.PetId == petId && v.VoidedAtUtc == null);

        PetVisitClinicScope.Apply(Query, clinicId, accessibleClinicIds);

        Query.OrderByDescending(v => v.ArrivedAtUtc)
            .ThenByDescending(v => v.Id)
            .Take(1)
            .AsNoTracking();
    }
}
