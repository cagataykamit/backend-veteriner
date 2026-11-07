using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Domain.ClinicSites;

namespace Backend.Veteriner.Application.ClinicSites;

/// <summary>Sitenin yapılandırmasız durumu; taslak kaydı ve diğer yazma uçlarının yanıtı (docs/CLINIC_SITE_API_CONTRACT.md §2).</summary>
public sealed record ClinicSiteStateDto(
    Guid SiteId,
    Guid ClinicId,
    string Slug,
    string PublicPath,
    bool IsTemporarilyClosed,
    int? PublishedVersionNumber,
    DateTime? PublishedAtUtc,
    bool HasUnpublishedChanges,
    DateTime DraftUpdatedAtUtc,
    string RowVersion);

/// <summary>Panelin site ekranı. Site yoksa <see cref="Exists"/> yanlış ve <see cref="SuggestedSlug"/> dolu; ilk taslak kaydı siteyi oluşturur.</summary>
public sealed record ClinicSiteDetailDto(
    bool Exists,
    string? SuggestedSlug,
    ClinicSiteStateDto? Site,
    JsonElement? Draft,
    bool IsClosedBySubscription,
    IReadOnlyList<string> MissingForPublish,
    IReadOnlyList<Guid> MissingImageIds,
    IReadOnlyList<ClinicSiteImageDto> Images);

/// <summary><see cref="Reference"/> yapılandırmada görsel alanına yazılan değerdir (<c>site-image:{id}</c>).</summary>
public sealed record ClinicSiteImageDto(Guid Id, string Reference, string ContentType, long SizeBytes, DateTime CreatedAtUtc);

public sealed record ClinicSiteVersionDto(
    int VersionNumber,
    bool IsPublished,
    DateTime PublishedAtUtc,
    Guid PublishedByUserId,
    string? PublishedByName);

public sealed record ClinicSiteAddressAvailabilityDto(
    string Slug,
    bool IsAvailable,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ClinicSiteAddressUnavailableReason? Reason);

public enum ClinicSiteAddressUnavailableReason
{
    Invalid = 0,
    Reserved = 1,
    Taken = 2,
}

/// <summary>Görsel içeriği akışı; hem panel önizlemesi hem herkese açık uç kullanır.</summary>
public sealed record ClinicSiteImageContent(Stream Content, string ContentType, string Sha256, long SizeBytes);

public enum PublicSiteStatus
{
    Published = 0,
    Redirect = 1,
    Closed = 2,
}

/// <summary>
/// Herkese açık site yanıtı (ADR-045 K8/K14). Yalnız yayınlanmış sürüm ve klinik kaydının izinli alanları döner; kiracı, klinik
/// ve site kimliği, taslak, kullanıcı ve hasta verisi dönmez. <see cref="PublicSiteStatus.Closed"/> nedeni ayırt etmez.
/// </summary>
public sealed record PublicSiteDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PublicSiteStatus Status,
    string Slug,
    string? RedirectToSlug,
    string? Message,
    int? VersionNumber,
    DateTime? PublishedAtUtc,
    JsonElement? Config,
    PublicSiteClinicDto? Clinic,
    PublicSiteOnlineBookingDto? OnlineBooking);

/// <summary>K8: klinik kaydından okunan alanlar.</summary>
public sealed record PublicSiteClinicDto(
    string Name,
    string City,
    string? District,
    string? Address,
    string? Phone,
    string? Email,
    IReadOnlyList<ClinicWorkingHourDto> WorkingHours);

/// <summary>K11: online randevu açıksa kod ve bağlantı; kapalıysa ikisi de boş, düğme telefona döner.</summary>
public sealed record PublicSiteOnlineBookingDto(bool IsEnabled, string? BookingCode, string? BookingUrl);

public static class ClinicSiteMapping
{
    /// <summary>Site uygulamasının yayın kökü altındaki yol (ADR-045 K6).</summary>
    public static string PublicPath(string slug) => $"/k/{slug}";

    public static ClinicSiteStateDto ToState(ClinicSite site)
        => new(
            site.Id,
            site.ClinicId,
            site.Slug,
            PublicPath(site.Slug),
            site.IsTemporarilyClosed,
            site.PublishedVersionNumber,
            site.PublishedAtUtc,
            site.HasUnpublishedChanges,
            site.DraftUpdatedAtUtc,
            ExaminationRowVersion.Encode(site.RowVersion));

    public static ClinicSiteImageDto ToImageDto(ClinicSiteImage image)
        => new(image.Id, ClinicSiteConfig.ImageReference(image.Id), image.ContentType, image.SizeBytes, image.CreatedAtUtc);

    /// <summary>Doğrulanmış belgeyi yanıtta ham JSON olarak taşır; alanlar yorumlanmaz (K3).</summary>
    public static JsonElement ToElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
