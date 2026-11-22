using Ardalis.Specification;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Vaccinations;
using Backend.Veteriner.Application.Common.Specifications;
using Backend.Veteriner.Application.Vaccinations;

namespace Backend.Veteriner.Application.Pets.Specs;

/// <summary>
/// Vadesi <c>[dueFromUtc, dueBeforeUtc)</c> içindeki vadeli aşılar (planlı ya da karşılanmamış sonraki tarih,
/// <see cref="VaccinationDueRule.IsOpenDue(IReadOnlyCollection{Guid})"/>; gecikmiş penceresindekiler dahil); en erken vade önce, sınırsız.
/// </summary>
public sealed class OpenDueVaccinationsForPetSpec : Specification<Vaccination>
{
    public OpenDueVaccinationsForPetSpec(
        Guid tenantId,
        Guid? clinicId,
        Guid petId,
        DateTime dueFromUtc,
        DateTime dueBeforeUtc,
        IReadOnlyCollection<Guid>? accessibleClinicIds,
        IReadOnlyCollection<Guid>? fulfilledNextDueIds = null)
    {
        Query.Where(v => v.TenantId == tenantId
                         && v.PetId == petId
                         && v.DueAtUtc != null
                         && v.DueAtUtc >= dueFromUtc
                         && v.DueAtUtc < dueBeforeUtc);
        Query.Where(VaccinationDueRule.IsOpenDue(fulfilledNextDueIds));
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
