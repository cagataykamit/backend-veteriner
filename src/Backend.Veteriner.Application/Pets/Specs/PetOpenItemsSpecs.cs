using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Application.Common.Specifications;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>Vadesi <paramref name="dueBeforeUtc"/>'den önce olan planlı aşılar (gecikmiş dahil); en erken vade önce, sınırsız.</summary>
public sealed class ScheduledVaccinationsDueForPetSpec : Specification<Vaccination>
{
    public ScheduledVaccinationsDueForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        DateTime dueBeforeUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(v => v.TenantId == tenantId
                         && v.PetId == petId
                         && v.Status == VaccinationStatus.Scheduled
                         && v.DueAtUtc != null
                         && v.DueAtUtc < dueBeforeUtc);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderBy(v => v.DueAtUtc)
            .ThenBy(v => v.Id)
            .AsNoTracking();
    }
}

/// <summary>
/// Hayvanın açık lab kayıtları, sınırsız: sonucu bekleyen (<c>Requested</c>) veya yorum bekleyen
/// (<see cref="LabResult.IsAwaitingInterpretation"/>). İptal edilmiş kayıt girmez. En eski istek önce.
/// </summary>
public sealed class OpenLabResultsForPetSpec : Specification<LabResult>
{
    public OpenLabResultsForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        bool awaitingInterpretation,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(l => l.TenantId == tenantId && l.PetId == petId);
        if (awaitingInterpretation)
            Query.Where(LabResult.IsAwaitingInterpretation);
        else
            Query.Where(l => l.Status == LabResultStatus.Requested);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderBy(l => l.RequestedAtUtc)
            .ThenBy(l => l.Id)
            .AsNoTracking();
    }
}

public sealed record PendingChargeSuggestionAmountRow(decimal Quantity, decimal? SuggestedUnitPrice);

/// <summary>Hayvanın bekleyen (<c>Pending</c>) ücret önerisi kalemlerinin miktar ve fiyatı (sayı ve toplam bellekte).</summary>
public sealed class PendingChargeSuggestionAmountsForPetSpec : Specification<ChargeSuggestion, PendingChargeSuggestionAmountRow>
{
    public PendingChargeSuggestionAmountsForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.AsNoTracking();
        Query.Where(s => s.TenantId == tenantId && s.PetId == petId && s.Status == ChargeSuggestionStatus.Pending);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.Select(s => new PendingChargeSuggestionAmountRow(s.Quantity, s.SuggestedUnitPrice));
    }
}

/// <summary>Hayvanın şimdiden sonraki en yakın planlı randevusu.</summary>
public sealed class NextScheduledAppointmentForPetSpec : Specification<Appointment>
{
    public NextScheduledAppointmentForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        DateTime nowUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds)
    {
        Query.Where(a => a.TenantId == tenantId
                         && a.PetId == petId
                         && a.Status == AppointmentStatus.Scheduled
                         && a.ScheduledAtUtc >= nowUtc);
        Query.ApplyClinicScope(clinicId, accessibleClinicIds);
        Query.OrderBy(a => a.ScheduledAtUtc)
            .ThenBy(a => a.Id)
            .Take(1)
            .AsNoTracking();
    }
}
