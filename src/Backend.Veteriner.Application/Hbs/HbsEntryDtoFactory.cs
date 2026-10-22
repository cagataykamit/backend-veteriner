using Backend.Veteriner.Application.Clinics.Veterinarians;
using Backend.Veteriner.Application.Hbs.Contracts;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Vaccinations;

namespace Backend.Veteriner.Application.Hbs;

/// <summary>Reçete/aşı kaydının HBS alanlarından yanıt nesnesi; işaretleyenin görünen adı kullanıcı adı kuralıyla çözülür.</summary>
public static class HbsEntryDtoFactory
{
    public static async Task<HbsEntryDto> ForPrescriptionAsync(Prescription p, IClinicVeterinarianReader names, CancellationToken ct)
        => new(p.HbsStatus, p.HbsReferenceNumber, p.HbsNotRequiredReason, p.HbsMarkedAtUtc, p.HbsMarkedByUserId,
            await NameAsync(p.HbsMarkedByUserId, names, ct), IsTracked: true);

    public static async Task<HbsEntryDto> ForVaccinationAsync(Vaccination v, IClinicVeterinarianReader names, CancellationToken ct)
        => new(v.HbsStatus, v.HbsReferenceNumber, v.HbsNotRequiredReason, v.HbsMarkedAtUtc, v.HbsMarkedByUserId,
            await NameAsync(v.HbsMarkedByUserId, names, ct), HbsTracking.IsTracked(v));

    private static async Task<string?> NameAsync(Guid? userId, IClinicVeterinarianReader names, CancellationToken ct)
    {
        if (userId is not { } id)
            return null;

        return (await names.GetNamesAsync([id], ct)).TryGetValue(id, out var name) ? name : null;
    }
}
