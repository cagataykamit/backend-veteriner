using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.VetFinder.Public;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.VetFinder;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.VetFinder;

/// <summary>Kliniğin Veteriner bul kaydı (docs/VET_FINDER_API_CONTRACT.md §2); kayıt yoksa listelenme kapalı döner.</summary>
public sealed record GetClinicVetFinderListingQuery(Guid ClinicId) : IRequest<Result<ClinicVetFinderListingDto>>;

/// <summary>
/// Listelenmeyi açar/kapatır, hizmetleri ve isteğe bağlı konumu yazar (ADR-047 K4). Yetki klinik ayarlarıyla aynı; tüm paketlerde
/// (K1). İlk kayıtta herkese açık anahtar üretilir.
/// </summary>
public sealed record UpdateClinicVetFinderListingCommand(
    Guid ClinicId,
    bool IsListed,
    IReadOnlyList<string> Services,
    decimal? Latitude,
    decimal? Longitude)
    : IRequest<Result<ClinicVetFinderListingDto>>, IAuditableRequest
{
    public string AuditAction => "VetFinder.Listing.Update";
    public string? AuditTarget => $"ClinicId={ClinicId};IsListed={IsListed}";
}

public sealed class GetClinicVetFinderListingQueryValidator : AbstractValidator<GetClinicVetFinderListingQuery>
{
    public GetClinicVetFinderListingQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class UpdateClinicVetFinderListingCommandValidator : AbstractValidator<UpdateClinicVetFinderListingCommand>
{
    public UpdateClinicVetFinderListingCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Services).NotNull().Must(s => s.Count <= VetFinderServices.All.Count);
        RuleForEach(x => x.Services).NotEmpty().MaximumLength(40);
    }
}

/// <summary>Panel yanıtı: kayıt, kartı zayıflatan eksik klinik bilgileri ve kliniğin görüneceği sayfa.</summary>
internal static class ClinicVetFinderListingMapping
{
    public static async Task<ClinicVetFinderListingDto> ToDtoAsync(
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> workingHours,
        Guid tenantId,
        Guid clinicId,
        ClinicVetFinderListing? listing,
        CancellationToken ct)
    {
        var clinic = await clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        var hours = await workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, clinicId), ct);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(clinic?.District))
            missing.Add("district");
        if (string.IsNullOrWhiteSpace(clinic?.Address))
            missing.Add("address");
        if (string.IsNullOrWhiteSpace(clinic?.Phone))
            missing.Add("phone");
        if (!hours.Any(h => !h.IsClosed))
            missing.Add("workingHours");

        var citySlug = TurkishSlug.From(clinic?.City);
        var districtSlug = TurkishSlug.From(clinic?.District);
        var path = districtSlug.Length == 0 ? $"/veteriner/{citySlug}" : $"/veteriner/{citySlug}/{districtSlug}";

        return new ClinicVetFinderListingDto(
            clinicId,
            listing?.IsListed ?? false,
            listing?.Services ?? [],
            listing?.Latitude,
            listing?.Longitude,
            path,
            missing,
            VetFinderServices.All.Select(VetFinderServiceDto.From).ToList());
    }
}

public sealed class GetClinicVetFinderListingQueryHandler : IRequestHandler<GetClinicVetFinderListingQuery, Result<ClinicVetFinderListingDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicVetFinderListing> _listings;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _hours;

    public GetClinicVetFinderListingQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicVetFinderListing> listings,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> hours)
    {
        _access = access;
        _listings = listings;
        _clinics = clinics;
        _hours = hours;
    }

    public async ValueTask<Result<ClinicVetFinderListingDto>> Handle(GetClinicVetFinderListingQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicVetFinderListingDto>.Failure(access.Error);

        var listing = await _listings.FirstOrDefaultAsync(new ClinicVetFinderListingByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<ClinicVetFinderListingDto>.Success(
            await ClinicVetFinderListingMapping.ToDtoAsync(_clinics, _hours, access.Value, request.ClinicId, listing, ct));
    }
}

public sealed class UpdateClinicVetFinderListingCommandHandler
    : IRequestHandler<UpdateClinicVetFinderListingCommand, Result<ClinicVetFinderListingDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicVetFinderListing> _listings;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _hours;
    private readonly VetFinderDirectory _directory;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicVetFinderListingCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicVetFinderListing> listings,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> hours,
        VetFinderDirectory directory,
        TimeProvider timeProvider)
    {
        _access = access;
        _listings = listings;
        _clinics = clinics;
        _hours = hours;
        _directory = directory;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicVetFinderListingDto>> Handle(UpdateClinicVetFinderListingCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicVetFinderListingDto>.Failure(access.Error);
        var tenantId = access.Value;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var listing = await _listings.FirstOrDefaultAsync(new ClinicVetFinderListingByClinicSpec(tenantId, request.ClinicId), ct);
        var isNew = listing is null;
        listing ??= ClinicVetFinderListing.Create(tenantId, request.ClinicId, await _directory.NewUniqueKeyAsync(ct), utcNow);

        var updated = listing.Update(request.IsListed, request.Services, request.Latitude, request.Longitude, utcNow);
        if (!updated.IsSuccess)
            return Result<ClinicVetFinderListingDto>.Failure(updated.Error);

        if (isNew)
            await _listings.AddAsync(listing, ct);
        await _listings.SaveChangesAsync(ct);
        return Result<ClinicVetFinderListingDto>.Success(
            await ClinicVetFinderListingMapping.ToDtoAsync(_clinics, _hours, tenantId, request.ClinicId, listing, ct));
    }
}
