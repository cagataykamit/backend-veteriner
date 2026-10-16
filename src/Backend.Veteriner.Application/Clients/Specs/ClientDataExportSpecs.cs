using Ardalis.Specification;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Dashboard;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Clients.Specs;

// Müşteri veri kopyası (ADR-032 madde 2) okumaları: yalnız dışa verilen alanlar projekte edilir (iç not ve denetim alanları
// sorguya girmez). Hasta adı handler'da doldurulur. Sıra en yeni önce; take = üst sınır + 1 (kesilme tespiti için).

public sealed class PetsForClientDataExportSpec : Specification<Pet, ClientDataExportPetDto>
{
    public PetsForClientDataExportSpec(Guid tenantId, Guid clientId)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId);
        Query.OrderBy(p => p.Name).ThenBy(p => p.Id);
        Query.Select(p => new ClientDataExportPetDto(
            p.Id,
            p.Name,
            p.Species != null ? p.Species.Name : string.Empty,
            p.Breed,
            p.Gender,
            p.BirthDate,
            p.IsNeutered,
            p.MicrochipNumber,
            p.PassportOrTagNumber,
            p.Status,
            p.DeceasedOn));
    }
}

public sealed class AppointmentsForClientDataExportSpec : Specification<Appointment, ClientDataExportAppointmentDto>
{
    public AppointmentsForClientDataExportSpec(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, Guid[] petIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(a => a.TenantId == tenantId && petIds.Contains(a.PetId));
        DashboardSpecificationClinicScope.ApplyToAppointment(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(a => a.ScheduledAtUtc).ThenByDescending(a => a.Id).Take(take);
        Query.Select(a => new ClientDataExportAppointmentDto(
            a.ScheduledAtUtc, a.DurationMinutes, a.PetId, string.Empty, a.AppointmentType, a.Status));
    }
}

public sealed class ExaminationsForClientDataExportSpec : Specification<Examination, ClientDataExportExaminationDto>
{
    public ExaminationsForClientDataExportSpec(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, Guid[] petIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(e => e.TenantId == tenantId && petIds.Contains(e.PetId));
        DashboardSpecificationClinicScope.ApplyToExamination(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(e => e.ExaminedAtUtc).ThenByDescending(e => e.Id).Take(take);
        Query.Select(e => new ClientDataExportExaminationDto(
            e.ExaminedAtUtc, e.PetId, string.Empty, e.VisitReason, e.Assessment, e.Status));
    }
}

public sealed class VaccinationsForClientDataExportSpec : Specification<Vaccination, ClientDataExportVaccinationDto>
{
    public VaccinationsForClientDataExportSpec(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, Guid[] petIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(v => v.TenantId == tenantId && petIds.Contains(v.PetId));
        DashboardSpecificationClinicScope.ApplyToVaccination(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(v => v.AppliedAtUtc ?? v.DueAtUtc ?? v.CreatedAtUtc).ThenByDescending(v => v.Id).Take(take);
        Query.Select(v => new ClientDataExportVaccinationDto(
            v.VaccineName, v.AppliedAtUtc, v.DueAtUtc, v.PetId, string.Empty, v.Status));
    }
}

public sealed class TreatmentsForClientDataExportSpec : Specification<Treatment, ClientDataExportTreatmentDto>
{
    public TreatmentsForClientDataExportSpec(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, Guid[] petIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(t => t.TenantId == tenantId && petIds.Contains(t.PetId));
        DashboardSpecificationClinicScope.ApplyToTreatment(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(t => t.TreatmentDateUtc).ThenByDescending(t => t.Id).Take(take);
        Query.Select(t => new ClientDataExportTreatmentDto(t.TreatmentDateUtc, t.Title, t.PetId, string.Empty));
    }
}

public sealed class PrescriptionsForClientDataExportSpec : Specification<Prescription, ClientDataExportPrescriptionDto>
{
    public PrescriptionsForClientDataExportSpec(
        Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, Guid[] petIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(p => p.TenantId == tenantId && petIds.Contains(p.PetId));
        DashboardSpecificationClinicScope.ApplyToPrescription(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PrescribedAtUtc).ThenByDescending(p => p.Id).Take(take);
        Query.Select(p => new ClientDataExportPrescriptionDto(
            p.PrescribedAtUtc,
            p.Title,
            p.Items.OrderBy(i => i.SortOrder).Select(i => i.Name).ToList(),
            p.PetId,
            string.Empty));
    }
}

/// <summary>
/// İptal edilmiş ücretler de (işaretli) gelir: global iptal süzgeci bilinçli olarak devre dışı. Varlık okunur ki kalan/iptal
/// kuralı <see cref="Charge"/> üzerinde tek yerde kalsın.
/// </summary>
public sealed class ChargesForClientDataExportSpec : Specification<Charge>
{
    public ChargesForClientDataExportSpec(
        Guid tenantId, Guid clientId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, int take)
    {
        Query.AsNoTracking().IgnoreQueryFilters();
        Query.Where(c => c.TenantId == tenantId && c.ClientId == clientId);
        DashboardSpecificationClinicScope.ApplyToCharge(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id).Take(take);
    }
}

/// <summary>İptal edilmiş ödemeler de (işaretli) gelir: global iptal süzgeci bilinçli olarak devre dışı.</summary>
public sealed class PaymentsForClientDataExportSpec : Specification<Payment>
{
    public PaymentsForClientDataExportSpec(
        Guid tenantId, Guid clientId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, int take)
    {
        Query.AsNoTracking().IgnoreQueryFilters();
        Query.Where(p => p.TenantId == tenantId && p.ClientId == clientId);
        DashboardSpecificationClinicScope.ApplyToPayment(Query, clinicId, accessibleClinicIds);
        Query.OrderByDescending(p => p.PaidAtUtc).ThenByDescending(p => p.Id).Take(take);
    }
}
