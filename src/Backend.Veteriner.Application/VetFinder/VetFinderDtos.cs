using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.VetFinder;

namespace Backend.Veteriner.Application.VetFinder;

public sealed record VetFinderServiceDto(string Key, string Name)
{
    public static VetFinderServiceDto From(VetFinderService service) => new(service.Key, service.Name);
}

/// <summary>
/// Panel: kliniğin Veteriner bul kaydı (docs/VET_FINDER_API_CONTRACT.md §2). Kayıt yoksa listelenme kapalı ve anahtar boş döner.
/// <see cref="PublicPath"/> kliniğin görüneceği ilçe (ilçe yoksa il) sayfasıdır; <see cref="Missing"/> kartı zayıflatan eksik
/// klinik bilgileridir, listelenmeyi engellemez.
/// </summary>
public sealed record ClinicVetFinderListingDto(
    Guid ClinicId,
    bool IsListed,
    IReadOnlyList<string> Services,
    decimal? Latitude,
    decimal? Longitude,
    string PublicPath,
    IReadOnlyList<string> Missing,
    IReadOnlyList<VetFinderServiceDto> AvailableServices);

/// <summary>Herkese açık il ve ilçe listesi: yalnız listelenen kliniği olan yerler (§3).</summary>
public sealed record VetFinderRegionsDto(
    int TotalClinics,
    int MinClinicsToIndex,
    IReadOnlyList<VetFinderCityDto> Cities,
    IReadOnlyList<VetFinderServiceDto> Services);

public sealed record VetFinderCityDto(string Slug, string Name, int ClinicCount, IReadOnlyList<VetFinderDistrictDto> Districts);

public sealed record VetFinderDistrictDto(string Slug, string Name, int ClinicCount, IReadOnlyList<VetFinderServiceCountDto> Services);

public sealed record VetFinderServiceCountDto(string Key, int ClinicCount);

public sealed record VetFinderPlaceDto(string Slug, string Name);

/// <summary>
/// Klinik listesi (§4). <see cref="Indexable"/> K8 kuralıdır: "şu an açık" filtresi ve konum hariç eşleşen klinik sayısı en az
/// <see cref="VetFinderPages.MinClinicsToIndex"/>. <see cref="SuggestedDistricts"/> yalnız ilçe sayfasında sonuç yokken dolar.
/// </summary>
public sealed record VetFinderClinicListDto(
    VetFinderPlaceDto? City,
    VetFinderPlaceDto? District,
    VetFinderServiceDto? Service,
    bool OpenNow,
    bool UsesDistance,
    bool AnyClinicHasLocation,
    bool Indexable,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<VetFinderClinicCardDto> Clinics,
    IReadOnlyList<VetFinderDistrictSuggestionDto> SuggestedDistricts);

/// <summary>
/// Kart: kliniğin kendi verdiği bilgi. Kiracı, klinik ve site kimliği, e-posta, açıklama ve hasta verisi dönmez; konumun kendisi
/// dönmez, yalnız ziyaretçi konum verdiyse mesafe. <see cref="SitePath"/> yayında ve açık site varsa dolar.
/// </summary>
public sealed record VetFinderClinicCardDto(
    string Key,
    string Name,
    string City,
    string CitySlug,
    string? District,
    string? DistrictSlug,
    string? Address,
    string? Phone,
    IReadOnlyList<VetFinderServiceDto> Services,
    IReadOnlyList<ClinicWorkingHourDto> WorkingHours,
    bool IsOpenNow,
    double? DistanceKm,
    string? SitePath,
    string? BookingUrl,
    bool ShowsNextSlot);

public sealed record VetFinderDistrictSuggestionDto(string Slug, string Name, int ClinicCount);

/// <summary>Kart başına en yakın müsait saat (§5): bilinmeyen anahtar ya da gösterilmeyen saat <c>null</c>, neden ayırt edilmez.</summary>
public sealed record VetFinderNextSlotsDto(IReadOnlyList<VetFinderNextSlotItemDto> Items);

public sealed record VetFinderNextSlotItemDto(string Key, NextAvailableSlotDto? Slot);
