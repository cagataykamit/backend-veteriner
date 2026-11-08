using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Application.Appointments.Availability;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.ClinicSites;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VetFinder;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.VetFinder.Public;

/// <summary>İl ve ilçe listesi; yalnız listelenen kliniği olan yerler (kimliksiz; TIER-015; docs/VET_FINDER_API_CONTRACT.md §3).</summary>
public sealed record GetVetFinderRegionsQuery : IRequest<Result<VetFinderRegionsDto>>;

/// <summary>
/// İl/ilçe, hizmet ve "şu an açık" filtresiyle klinik listesi (kimliksiz; §4). Konum yalnız sıralama içindir, iki ondalığa
/// yuvarlanır (yaklaşık 1 km), saklanmaz ve yanıtta dönmez.
/// </summary>
public sealed record GetVetFinderClinicsQuery(
    string? City,
    string? District,
    string? Service,
    bool OpenNow,
    int Page,
    double? Latitude,
    double? Longitude) : IRequest<Result<VetFinderClinicListDto>>;

/// <summary>Kartların en yakın müsait saati, bir istekte bir sayfa kart (kimliksiz; §5).</summary>
public sealed record GetVetFinderNextSlotsQuery(IReadOnlyList<string> Keys) : IRequest<Result<VetFinderNextSlotsDto>>;

internal static class VetFinderPublicErrors
{
    public static readonly Error NotFound = new("VetFinder.NotFound", "Bu bölgede listelenen klinik bulunamadı.");
}

public sealed class GetVetFinderRegionsQueryHandler : IRequestHandler<GetVetFinderRegionsQuery, Result<VetFinderRegionsDto>>
{
    private readonly VetFinderDirectory _directory;

    public GetVetFinderRegionsQueryHandler(VetFinderDirectory directory) => _directory = directory;

    public async ValueTask<Result<VetFinderRegionsDto>> Handle(GetVetFinderRegionsQuery request, CancellationToken ct)
    {
        var entries = await _directory.LoadAsync(ct);
        var cities = VetFinderRegions.Group(entries)
            .Select(c => new VetFinderCityDto(
                c.Slug,
                c.Name,
                c.Entries.Count,
                c.Districts.Select(d => new VetFinderDistrictDto(
                    d.Slug,
                    d.Name,
                    d.Entries.Count,
                    VetFinderServices.All
                        .Select(s => new VetFinderServiceCountDto(s.Key, d.Entries.Count(e => e.Services.Contains(s.Key))))
                        .Where(s => s.ClinicCount > 0)
                        .ToList())).ToList()))
            .ToList();

        return Result<VetFinderRegionsDto>.Success(new VetFinderRegionsDto(
            entries.Count, VetFinderPages.MinClinicsToIndex, cities, VetFinderServices.All.Select(VetFinderServiceDto.From).ToList()));
    }
}

public sealed class GetVetFinderClinicsQueryValidator : AbstractValidator<GetVetFinderClinicsQuery>
{
    public GetVetFinderClinicsQueryValidator()
    {
        RuleFor(x => x.City).MaximumLength(80);
        RuleFor(x => x.District).MaximumLength(80);
        RuleFor(x => x.Service).MaximumLength(40);
        RuleFor(x => x.District).Empty().When(x => string.IsNullOrWhiteSpace(x.City)).WithMessage("İlçe için il gerekir.");
        RuleFor(x => x.Page).InclusiveBetween(1, 1000);
        RuleFor(x => x.Latitude).InclusiveBetween(-90.0, 90.0);
        RuleFor(x => x.Longitude).InclusiveBetween(-180.0, 180.0);
        RuleFor(x => x).Must(x => x.Latitude.HasValue == x.Longitude.HasValue)
            .WithName("location").WithMessage("Konum için enlem ve boylam birlikte verilmelidir.");
    }
}

public sealed class GetVetFinderClinicsQueryHandler : IRequestHandler<GetVetFinderClinicsQuery, Result<VetFinderClinicListDto>>
{
    private readonly VetFinderDirectory _directory;
    private readonly TimeProvider _timeProvider;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public GetVetFinderClinicsQueryHandler(
        VetFinderDirectory directory,
        TimeProvider timeProvider,
        IOptions<OwnerPortalOptions> portal,
        IOptions<AppOptions> app)
    {
        _directory = directory;
        _timeProvider = timeProvider;
        _portal = portal.Value;
        _app = app.Value;
    }

    public async ValueTask<Result<VetFinderClinicListDto>> Handle(GetVetFinderClinicsQuery request, CancellationToken ct)
    {
        var entries = await _directory.LoadAsync(ct);
        var cities = VetFinderRegions.Group(entries);

        VetFinderCityGroup? city = null;
        VetFinderDistrictGroup? district = null;
        VetFinderService? service = null;
        if (Slug(request.City) is { } citySlug && (city = cities.FirstOrDefault(c => c.Slug == citySlug)) is null)
            return Result<VetFinderClinicListDto>.Failure(VetFinderPublicErrors.NotFound);
        if (Slug(request.District) is { } districtSlug && (district = city!.Districts.FirstOrDefault(d => d.Slug == districtSlug)) is null)
            return Result<VetFinderClinicListDto>.Failure(VetFinderPublicErrors.NotFound);
        if (Slug(request.Service) is { } serviceKey && (service = VetFinderServices.Find(serviceKey)) is null)
            return Result<VetFinderClinicListDto>.Failure(VetFinderPublicErrors.NotFound);

        bool Offers(VetFinderEntry e) => service is null || e.Services.Contains(service.Key);

        var localNow = OperationDayBounds.ToLocal(_timeProvider.GetUtcNow().UtcDateTime);
        var matching = (district?.Entries ?? city?.Entries ?? entries)
            .Where(Offers)
            .Select(e => (Entry: e, IsOpen: ClinicWorkingHour.IsOpenAt(e.Hours, localNow)))
            .ToList();
        var filtered = request.OpenNow ? matching.Where(x => x.IsOpen).ToList() : matching;

        // Yaklaşık 1 km: sıralamaya yeter, kesin konum işlenmez.
        GeoPoint? visitor = request is { Latitude: { } lat, Longitude: { } lng }
            ? new GeoPoint(Math.Round(lat, 2), Math.Round(lng, 2))
            : null;
        var ranked = VetFinderRanking.Order(
            filtered,
            x => new VetFinderRankInput(x.IsOpen, x.Entry.CitySlug, x.Entry.DistrictSlug, x.Entry.Location, x.Entry.Name, x.Entry.Key),
            new VetFinderRankReference(city?.Slug, district?.Slug, visitor));

        var cards = ranked
            .Skip((request.Page - 1) * VetFinderPages.PageSize)
            .Take(VetFinderPages.PageSize)
            .Select(x => ToCard(x.Item.Entry, x.Item.IsOpen, x.DistanceKm))
            .ToList();

        var suggestions = filtered.Count == 0 && district is not null
            ? city!.Districts
                .Where(d => d.Slug != district.Slug)
                .Select(d => new VetFinderDistrictSuggestionDto(d.Slug, d.Name, d.Entries.Count(Offers)))
                .Where(d => d.ClinicCount > 0)
                .OrderByDescending(d => d.ClinicCount)
                .ThenBy(d => TurkishTextKey.Normalize(d.Name), StringComparer.Ordinal)
                .Take(VetFinderPages.MaxSuggestedDistricts)
                .ToList()
            : [];

        return Result<VetFinderClinicListDto>.Success(new VetFinderClinicListDto(
            city is null ? null : new VetFinderPlaceDto(city.Slug, city.Name),
            district is null ? null : new VetFinderPlaceDto(district.Slug, district.Name),
            service is null ? null : VetFinderServiceDto.From(service),
            request.OpenNow,
            visitor is not null,
            filtered.Any(x => x.Entry.Location is not null),
            matching.Count >= VetFinderPages.MinClinicsToIndex,
            filtered.Count,
            request.Page,
            VetFinderPages.PageSize,
            cards,
            suggestions));
    }

    private VetFinderClinicCardDto ToCard(VetFinderEntry e, bool isOpen, double? distanceKm)
        => new(
            e.Key,
            e.Name,
            e.City,
            e.CitySlug,
            e.District,
            e.DistrictSlug,
            e.Address,
            e.Phone,
            e.Services.Select(VetFinderServices.Find).OfType<VetFinderService>().Select(VetFinderServiceDto.From).ToList(),
            e.Hours.Select(h => new ClinicWorkingHourDto(h.DayOfWeek, h.IsClosed, h.OpensAt, h.ClosesAt, h.BreakStartsAt, h.BreakEndsAt)).ToList(),
            isOpen,
            distanceKm is { } km ? Math.Round(km, 1) : null,
            e.SiteSlug is null ? null : ClinicSiteMapping.PublicPath(e.SiteSlug),
            e.BookingCode is null ? null : ClinicOnlineBookingMapping.BookingLink(_portal, _app, e.BookingCode),
            e.BookingCode is not null && e.PublishesNextSlots);

    private static string? Slug(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

public sealed class GetVetFinderNextSlotsQueryValidator : AbstractValidator<GetVetFinderNextSlotsQuery>
{
    public GetVetFinderNextSlotsQueryValidator()
    {
        RuleFor(x => x.Keys).NotEmpty().Must(k => k.Count <= VetFinderPages.MaxNextSlotKeys)
            .WithMessage($"En çok {VetFinderPages.MaxNextSlotKeys} anahtar verilebilir.");
        RuleForEach(x => x.Keys).NotEmpty().MaximumLength(ClinicVetFinderListing.PublicKeyLength);
    }
}

/// <summary>
/// Her kart için TIER-017 yolu: online randevu kapısı (<see cref="OnlineBookingClinicResolver"/>) ve
/// <see cref="AppointmentRequestIntake.GetNextSlotsAsync"/>'ın ilk saati; ikinci bir müsaitlik hesabı yoktur. Hesap pahalı olduğu için
/// klinik başına <see cref="CacheDuration"/> kısa önbellek kullanılır; listelenme her istekte yeniden denetlenir, önbellek yalnız saati
/// tutar. Dolan saat kartta en çok bu süre görünebilir; talep formu müsaitliği yine denetler.
/// </summary>
public sealed class GetVetFinderNextSlotsQueryHandler : IRequestHandler<GetVetFinderNextSlotsQuery, Result<VetFinderNextSlotsDto>>
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly VetFinderDirectory _directory;
    private readonly OnlineBookingClinicResolver _bookingResolver;
    private readonly AppointmentRequestIntake _intake;
    private readonly ITenantScope _tenantScope;
    private readonly IMemoryCache _cache;

    public GetVetFinderNextSlotsQueryHandler(
        VetFinderDirectory directory,
        OnlineBookingClinicResolver bookingResolver,
        AppointmentRequestIntake intake,
        ITenantScope tenantScope,
        IMemoryCache cache)
    {
        _directory = directory;
        _bookingResolver = bookingResolver;
        _intake = intake;
        _tenantScope = tenantScope;
        _cache = cache;
    }

    public async ValueTask<Result<VetFinderNextSlotsDto>> Handle(GetVetFinderNextSlotsQuery request, CancellationToken ct)
    {
        var keys = request.Keys.Select(k => k.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var listed = (await _directory.FindListedAsync(keys, ct)).ToDictionary(c => c.Key, StringComparer.Ordinal);

        var items = new List<VetFinderNextSlotItemDto>(keys.Count);
        foreach (var key in keys)
        {
            var slot = listed.TryGetValue(key, out var clinic) ? await NextSlotAsync(clinic, ct) : null;
            items.Add(new VetFinderNextSlotItemDto(key, slot));
        }

        return Result<VetFinderNextSlotsDto>.Success(new VetFinderNextSlotsDto(items));
    }

    private async Task<NextAvailableSlotDto?> NextSlotAsync(VetFinderListedClinic clinic, CancellationToken ct)
        => await _cache.GetOrCreateAsync($"vet-finder:next-slot:{clinic.TenantId:N}:{clinic.ClinicId:N}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            using var tenant = _tenantScope.UseTenant(clinic.TenantId);
            var booking = await _bookingResolver.ResolveByClinicAsync(clinic.TenantId, clinic.ClinicId, ct);
            return booking.IsSuccess ? (await _intake.GetNextSlotsAsync(booking.Value!, 1, ct)).FirstOrDefault() : null;
        });
}
