using System.Globalization;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Clinics;

internal static class ClinicListItemMappings
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Liste satırları; kısa adres için kliniklerin mükellef ayarları tek sorguda okunur.
    /// </summary>
    public static async Task<IReadOnlyList<ClinicListItemDto>> ToListItemDtosAsync(
        this IReadOnlyCollection<Clinic> clinics,
        IReadRepository<ClinicTaxpayerSettings> taxpayerSettings,
        Guid tenantId,
        CancellationToken ct)
    {
        if (clinics.Count == 0)
            return [];

        var clinicIds = clinics.Select(c => c.Id).ToArray();
        var byClinic = (await taxpayerSettings.ListAsync(new ClinicTaxpayerSettingsByClinicIdsSpec(tenantId, clinicIds), ct))
            .ToDictionary(s => s.ClinicId);

        return clinics
            .Select(c => new ClinicListItemDto(
                c.Id, c.TenantId, c.Name, c.City, c.IsActive, c.Phone, c.Email,
                ShortAddress(c, byClinic.GetValueOrDefault(c.Id))))
            .ToArray();
    }

    /// <summary>
    /// "İlçe, İl" (ör. "Kadıköy, İstanbul"); il klinik şehridir. Klinikte ilçe alanı yok, ilçe mükellef
    /// adresinden (ADR-036) alınır; mükellef ili klinik şehrinden farklıysa ilçe başka yere aittir, yalnız il döner.
    /// </summary>
    internal static string ShortAddress(Clinic clinic, ClinicTaxpayerSettings? taxpayer)
    {
        if (taxpayer?.District is not { } district)
            return clinic.City;

        var sameProvince = taxpayer.Province is null
            || string.Compare(taxpayer.Province, clinic.City, Turkish, CompareOptions.IgnoreCase) == 0;
        return sameProvince ? $"{district}, {clinic.City}" : clinic.City;
    }
}
