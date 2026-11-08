using Backend.Veteriner.Application.AppointmentRequests;
using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.OnlineBooking;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.ClinicSites.Public;

/// <summary>Adresle yayınlanmış site (kimliksiz; ADR-045; docs/CLINIC_SITE_API_CONTRACT.md §6).</summary>
public sealed record GetPublicSiteQuery(string Slug) : IRequest<Result<PublicSiteDto>>;

/// <summary>Yayındaki sürümün başvurduğu görsel (kimliksiz). Taslak görseli ve kapalı sitenin görseli 404.</summary>
public sealed record GetPublicSiteImageQuery(string Slug, Guid ImageId) : IRequest<Result<ClinicSiteImageContent>>;

/// <summary>
/// Kliniğin online randevuya açtığı en yakın müsait saatler (kimliksiz; TIER-017; docs/ONLINE_APPOINTMENT_API_CONTRACT.md §12).
/// Yalnız gün ve saat döner.
/// </summary>
public sealed record GetPublicSiteNextSlotsQuery(string Slug, int Count) : IRequest<Result<PublicSiteNextSlotsDto>>;

public sealed class GetPublicSiteQueryHandler : IRequestHandler<GetPublicSiteQuery, Result<PublicSiteDto>>
{
    /// <summary>Geçici kapatma ve abonelik kapanması dışarıya aynı metinle görünür (K13/K14).</summary>
    public const string ClosedMessage = "Bu site şu anda yayında değil.";

    private readonly PublicSiteResolver _resolver;
    private readonly IReadRepository<ClinicSiteVersion> _versions;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;
    private readonly IReadRepository<ClinicOnlineBookingSettings> _bookingSettings;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public GetPublicSiteQueryHandler(
        PublicSiteResolver resolver,
        IReadRepository<ClinicSiteVersion> versions,
        IReadRepository<ClinicWorkingHour> workingHours,
        IReadRepository<ClinicOnlineBookingSettings> bookingSettings,
        IOptions<OwnerPortalOptions> portal,
        IOptions<AppOptions> app)
    {
        _resolver = resolver;
        _versions = versions;
        _workingHours = workingHours;
        _bookingSettings = bookingSettings;
        _portal = portal.Value;
        _app = app.Value;
    }

    public async ValueTask<Result<PublicSiteDto>> Handle(GetPublicSiteQuery request, CancellationToken ct)
    {
        var resolved = await _resolver.ResolveAsync(request.Slug, ct);
        if (!resolved.IsSuccess)
            return Result<PublicSiteDto>.Failure(resolved.Error);
        var scope = resolved.Value!;

        return scope.Status switch
        {
            PublicSiteStatus.Redirect => Result<PublicSiteDto>.Success(new PublicSiteDto(
                PublicSiteStatus.Redirect, scope.Slug, scope.Site.Slug, null, null, null, null, null, null)),
            PublicSiteStatus.Closed => Result<PublicSiteDto>.Success(new PublicSiteDto(
                PublicSiteStatus.Closed, scope.Slug, null, ClosedMessage, null, null, null, null, null)),
            _ => await BuildPublishedAsync(scope, ct),
        };
    }

    private async Task<Result<PublicSiteDto>> BuildPublishedAsync(PublicSiteScope scope, CancellationToken ct)
    {
        var site = scope.Site;
        var clinic = scope.Clinic;

        var version = await _versions.FirstOrDefaultAsync(
            new ClinicSiteVersionByNumberSpec(site.TenantId, site.Id, site.PublishedVersionNumber!.Value), ct);
        if (version is null)
            return Result<PublicSiteDto>.Failure(ClinicSiteErrors.PublicNotFound);

        var hours = await _workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(site.TenantId, clinic.Id), ct);
        var settings = await _bookingSettings.FirstOrDefaultAsync(new ClinicOnlineBookingSettingsByClinicSpec(site.TenantId, clinic.Id), ct);

        var booking = settings is { IsEnabled: true }
            ? new PublicSiteOnlineBookingDto(true, settings.BookingCode, ClinicOnlineBookingMapping.BookingLink(_portal, _app, settings.BookingCode))
            : new PublicSiteOnlineBookingDto(false, null, null);

        return Result<PublicSiteDto>.Success(new PublicSiteDto(
            PublicSiteStatus.Published,
            scope.Slug,
            null,
            null,
            version.VersionNumber,
            version.PublishedAtUtc,
            ClinicSiteMapping.ToElement(version.ConfigJson),
            new PublicSiteClinicDto(
                clinic.Name,
                clinic.City,
                clinic.District,
                clinic.Address,
                clinic.Phone,
                clinic.Email,
                hours.Select(h => new ClinicWorkingHourDto(h.DayOfWeek, h.IsClosed, h.OpensAt, h.ClosesAt, h.BreakStartsAt, h.BreakEndsAt)).ToList()),
            booking));
    }
}

public sealed class GetPublicSiteNextSlotsQueryValidator : AbstractValidator<GetPublicSiteNextSlotsQuery>
{
    public GetPublicSiteNextSlotsQueryValidator()
        => RuleFor(x => x.Count).InclusiveBetween(1, AppointmentRequestIntake.MaxNextSlotCount);
}

/// <summary>
/// Site kapısı (<see cref="PublicSiteResolver"/>) ve online randevu kapısı (<see cref="OnlineBookingClinicResolver"/>) ardından
/// talep akışının müsaitliği. Olmayan site 404; kapalı site, kapalı online randevu ya da gösterimi kapatılmış klinik boş liste.
/// </summary>
public sealed class GetPublicSiteNextSlotsQueryHandler : IRequestHandler<GetPublicSiteNextSlotsQuery, Result<PublicSiteNextSlotsDto>>
{
    private static readonly PublicSiteNextSlotsDto None = new([]);

    private readonly PublicSiteResolver _siteResolver;
    private readonly OnlineBookingClinicResolver _bookingResolver;
    private readonly AppointmentRequestIntake _intake;

    public GetPublicSiteNextSlotsQueryHandler(
        PublicSiteResolver siteResolver,
        OnlineBookingClinicResolver bookingResolver,
        AppointmentRequestIntake intake)
    {
        _siteResolver = siteResolver;
        _bookingResolver = bookingResolver;
        _intake = intake;
    }

    public async ValueTask<Result<PublicSiteNextSlotsDto>> Handle(GetPublicSiteNextSlotsQuery request, CancellationToken ct)
    {
        var resolved = await _siteResolver.ResolveAsync(request.Slug, ct);
        if (!resolved.IsSuccess)
            return Result<PublicSiteNextSlotsDto>.Failure(resolved.Error);
        var scope = resolved.Value!;

        if (scope.Status == PublicSiteStatus.Closed)
            return Result<PublicSiteNextSlotsDto>.Success(None);

        var booking = await _bookingResolver.ResolveByClinicAsync(scope.Site.TenantId, scope.Clinic.Id, ct);
        if (!booking.IsSuccess)
            return Result<PublicSiteNextSlotsDto>.Success(None);

        return Result<PublicSiteNextSlotsDto>.Success(new PublicSiteNextSlotsDto(await _intake.GetNextSlotsAsync(booking.Value!, request.Count, ct)));
    }
}

public sealed class GetPublicSiteImageQueryHandler : IRequestHandler<GetPublicSiteImageQuery, Result<ClinicSiteImageContent>>
{
    private readonly PublicSiteResolver _resolver;
    private readonly IReadRepository<ClinicSiteVersion> _versions;
    private readonly IReadRepository<ClinicSiteImage> _images;
    private readonly ClinicSiteImageContentReader _reader;

    public GetPublicSiteImageQueryHandler(
        PublicSiteResolver resolver,
        IReadRepository<ClinicSiteVersion> versions,
        IReadRepository<ClinicSiteImage> images,
        ClinicSiteImageContentReader reader)
    {
        _resolver = resolver;
        _versions = versions;
        _images = images;
        _reader = reader;
    }

    public async ValueTask<Result<ClinicSiteImageContent>> Handle(GetPublicSiteImageQuery request, CancellationToken ct)
    {
        var resolved = await _resolver.ResolveAsync(request.Slug, ct);
        if (!resolved.IsSuccess)
            return Result<ClinicSiteImageContent>.Failure(resolved.Error);
        var scope = resolved.Value!;

        // Kapalı site görsel sunmaz; açık sitede eski adresten gelen istek yeni adresteki gibi yanıtlanır (önbellekteki sayfalar kırılmasın).
        if (scope.Status == PublicSiteStatus.Closed)
            return NotFound();
        var site = scope.Site;

        var referenced = await _versions.FirstOrDefaultAsync(
            new ClinicSiteVersionReferencedImagesSpec(site.TenantId, site.Id, site.PublishedVersionNumber!.Value), ct);
        if (referenced is null || !ClinicSiteVersion.ListContainsImage(referenced, request.ImageId))
            return NotFound();

        var image = await _images.FirstOrDefaultAsync(new ClinicSiteImageByIdSpec(site.TenantId, site.Id, request.ImageId), ct);
        return image is null ? NotFound() : await _reader.OpenAsync(image, ct);
    }

    private static Result<ClinicSiteImageContent> NotFound() => Result<ClinicSiteImageContent>.Failure(ClinicSiteErrors.PublicNotFound);
}
