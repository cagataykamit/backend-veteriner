using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics;

internal static class ClinicListItemMappings
{
    public static ClinicListItemDto ToListItemDto(this Clinic c)
        => new(c.Id, c.TenantId, c.Name, c.City, c.District, c.IsActive, c.Phone, c.Email, ShortAddress(c));

    /// <summary>"İlçe, İl" (ör. "Kadıköy, İstanbul"); ilçe yoksa yalnız il.</summary>
    internal static string ShortAddress(Clinic clinic)
        => clinic.District is { } district ? $"{district}, {clinic.City}" : clinic.City;
}
