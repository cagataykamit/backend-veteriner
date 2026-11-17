using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.Vaccinations.Calendar;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Vaccinations.Specs;

public sealed record VaccinationCalendarRow(
    Guid Id,
    Guid ClinicId,
    Guid PetId,
    Guid? VaccineDefinitionId,
    string VaccineName,
    VaccinationStatus Status,
    DateTime? AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes);

/// <summary>
/// Klinikte <c>[from, to)</c> aralığına düşen aşı kayıtları: vadesi aralıkta olan planlı ya da uygulanmış (sonraki tarih) kayıt ve
/// aralıkta uygulanmış kayıt. İptal edilen kayıt girmez.
/// </summary>
public sealed class VaccinationsCalendarSpec : Specification<Vaccination, VaccinationCalendarRow>
{
    public VaccinationsCalendarSpec(Guid tenantId, Guid clinicId, DateTime fromUtc, DateTime toUtc)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId
                         && v.ClinicId == clinicId
                         && v.Status != VaccinationStatus.Cancelled
                         && ((v.DueAtUtc != null && v.DueAtUtc >= fromUtc && v.DueAtUtc < toUtc)
                             || (v.Status == VaccinationStatus.Applied && v.AppliedAtUtc != null && v.AppliedAtUtc >= fromUtc && v.AppliedAtUtc < toUtc)));
        Query.OrderBy(v => v.Id);
        Query.Select(v => new VaccinationCalendarRow(
            v.Id,
            v.ClinicId,
            v.PetId,
            v.VaccineDefinitionId,
            v.VaccineName,
            v.Status,
            v.AppliedAtUtc,
            v.DueAtUtc,
            v.Notes));
    }
}

/// <summary>
/// Hayvanların <paramref name="appliedAfterUtc"/>'den sonra uygulanmış aşıları (sonraki tarihin karşılanıp karşılanmadığı için);
/// kullanıcının okuyabildiği kliniklerden.
/// </summary>
public sealed class AppliedVaccinationDosesForPetsSpec : Specification<Vaccination, VaccinationDose>
{
    public AppliedVaccinationDosesForPetsSpec(
        Guid tenantId,
        IReadOnlyCollection<Guid> petIds,
        DateTime appliedAfterUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        var ids = petIds.ToArray();
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId
                         && ids.Contains(v.PetId)
                         && v.Status == VaccinationStatus.Applied
                         && v.AppliedAtUtc != null
                         && v.AppliedAtUtc > appliedAfterUtc);
        Query.ApplyClinicScope(null, accessibleClinicIds);
        Query.Select(v => new VaccinationDose(v.Id, v.PetId, v.VaccineDefinitionId, v.VaccineName, v.AppliedAtUtc!.Value));
    }
}

