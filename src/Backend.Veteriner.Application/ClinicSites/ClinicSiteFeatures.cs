using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.ClinicSites;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ClinicSites;

/// <summary>Panelin site ekranı (ADR-045; docs/CLINIC_SITE_API_CONTRACT.md §2). Site yoksa önerilen adres ve zorunlu liste döner.</summary>
public sealed record GetClinicSiteQuery(Guid ClinicId) : IRequest<Result<ClinicSiteDetailDto>>;

/// <summary>Adres uygunluğu (K10).</summary>
public sealed record CheckClinicSiteAddressQuery(Guid ClinicId, string? Slug) : IRequest<Result<ClinicSiteAddressAvailabilityDto>>;

public sealed record GetClinicSiteVersionsQuery(Guid ClinicId) : IRequest<Result<IReadOnlyList<ClinicSiteVersionDto>>>;

/// <summary>
/// Taslağı kaydeder; ilk kayıt siteyi önerilen adresle oluşturur (K2). Otomatik kayıt sık çalıştığı için denetim kaydı yazmaz.
/// Site varsa <see cref="RowVersion"/> zorunludur; eski sürümle yazım 409 döner.
/// </summary>
public sealed record SaveClinicSiteDraftCommand(Guid ClinicId, string? ConfigJson, string? RowVersion)
    : IRequest<Result<ClinicSiteStateDto>>;

/// <summary>Adresi değiştirir (K10).</summary>
public sealed record ChangeClinicSiteAddressCommand(Guid ClinicId, string? Slug)
    : IRequest<Result<ClinicSiteStateDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Address.Change";
    public string? AuditTarget => $"ClinicId={ClinicId};Slug={Slug}";
}

/// <summary>Taslağı yeni değişmez sürüm olarak yayınlar (K2/K9). <see cref="RowVersion"/> klinik gördüğünü yayınlasın diye zorunludur.</summary>
public sealed record PublishClinicSiteCommand(Guid ClinicId, string? RowVersion)
    : IRequest<Result<ClinicSiteStateDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Publish";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

/// <summary>Eski sürümü taslağa kopyalar; yayın değişmez (K2).</summary>
public sealed record RestoreClinicSiteVersionCommand(Guid ClinicId, int VersionNumber, string? RowVersion)
    : IRequest<Result<ClinicSiteStateDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Version.Restore";
    public string? AuditTarget => $"ClinicId={ClinicId};Version={VersionNumber}";
}

/// <summary>Geçici kapatma (K9); yayınlanan sürüm ve taslak durur.</summary>
public sealed record CloseClinicSiteCommand(Guid ClinicId)
    : IRequest<Result<ClinicSiteStateDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Close";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

public sealed record ReopenClinicSiteCommand(Guid ClinicId)
    : IRequest<Result<ClinicSiteStateDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicSite.Reopen";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

public sealed class GetClinicSiteQueryValidator : AbstractValidator<GetClinicSiteQuery>
{
    public GetClinicSiteQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class CheckClinicSiteAddressQueryValidator : AbstractValidator<CheckClinicSiteAddressQuery>
{
    public CheckClinicSiteAddressQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class GetClinicSiteVersionsQueryValidator : AbstractValidator<GetClinicSiteVersionsQuery>
{
    public GetClinicSiteVersionsQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class SaveClinicSiteDraftCommandValidator : AbstractValidator<SaveClinicSiteDraftCommand>
{
    public SaveClinicSiteDraftCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class ChangeClinicSiteAddressCommandValidator : AbstractValidator<ChangeClinicSiteAddressCommand>
{
    public ChangeClinicSiteAddressCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class PublishClinicSiteCommandValidator : AbstractValidator<PublishClinicSiteCommand>
{
    public PublishClinicSiteCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class RestoreClinicSiteVersionCommandValidator : AbstractValidator<RestoreClinicSiteVersionCommand>
{
    public RestoreClinicSiteVersionCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.VersionNumber).GreaterThan(0);
    }
}

public sealed class CloseClinicSiteCommandValidator : AbstractValidator<CloseClinicSiteCommand>
{
    public CloseClinicSiteCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class ReopenClinicSiteCommandValidator : AbstractValidator<ReopenClinicSiteCommand>
{
    public ReopenClinicSiteCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class GetClinicSiteQueryHandler : IRequestHandler<GetClinicSiteQuery, Result<ClinicSiteDetailDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IReadRepository<ClinicSiteImage> _images;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;
    private readonly ClinicSiteAddressBook _addressBook;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _subscription;

    public GetClinicSiteQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicSite> sites,
        IReadRepository<ClinicSiteImage> images,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> workingHours,
        ClinicSiteAddressBook addressBook,
        TenantSubscriptionEffectiveWriteEvaluator subscription)
    {
        _access = access;
        _sites = sites;
        _images = images;
        _clinics = clinics;
        _workingHours = workingHours;
        _addressBook = addressBook;
        _subscription = subscription;
    }

    public async ValueTask<Result<ClinicSiteDetailDto>> Handle(GetClinicSiteQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteDetailDto>.Failure(access.Error);
        var tenantId = access.Value;

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, request.ClinicId), ct);
        if (clinic is null)
            return Result<ClinicSiteDetailDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        var hours = await _workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, request.ClinicId), ct);
        var missing = ClinicSitePublishRequirements.Missing(clinic, hours);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
        {
            var suggested = await _addressBook.SuggestAsync(clinic.Name, ct);
            return Result<ClinicSiteDetailDto>.Success(new ClinicSiteDetailDto(false, suggested, null, null, false, missing, [], []));
        }

        var images = await _images.ListAsync(new ClinicSiteImagesBySiteSpec(tenantId, site.Id), ct);
        var readyIds = images.Select(i => i.Id).ToHashSet();
        var draft = ClinicSiteConfig.Parse(site.DraftConfigJson);
        var missingImageIds = draft.IsSuccess ? draft.Value!.ImageIds.Where(id => !readyIds.Contains(id)).ToList() : [];
        var closedBySubscription = !(await _subscription.EnsureWriteAllowedAsync(tenantId, ct)).IsSuccess;

        return Result<ClinicSiteDetailDto>.Success(new ClinicSiteDetailDto(
            true,
            null,
            ClinicSiteMapping.ToState(site),
            ClinicSiteMapping.ToElement(site.DraftConfigJson),
            closedBySubscription,
            missing,
            missingImageIds,
            images.Select(ClinicSiteMapping.ToImageDto).ToList()));
    }
}

public sealed class CheckClinicSiteAddressQueryHandler
    : IRequestHandler<CheckClinicSiteAddressQuery, Result<ClinicSiteAddressAvailabilityDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly ClinicSiteAddressBook _addressBook;

    public CheckClinicSiteAddressQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicSite> sites,
        ClinicSiteAddressBook addressBook)
    {
        _access = access;
        _sites = sites;
        _addressBook = addressBook;
    }

    public async ValueTask<Result<ClinicSiteAddressAvailabilityDto>> Handle(CheckClinicSiteAddressQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteAddressAvailabilityDto>.Failure(access.Error);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(access.Value, request.ClinicId), ct);
        return Result<ClinicSiteAddressAvailabilityDto>.Success(await _addressBook.CheckAsync(site?.Id, request.Slug, ct));
    }
}

public sealed class GetClinicSiteVersionsQueryHandler
    : IRequestHandler<GetClinicSiteVersionsQuery, Result<IReadOnlyList<ClinicSiteVersionDto>>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicSite> _sites;
    private readonly IReadRepository<ClinicSiteVersion> _versions;
    private readonly IReadRepository<User> _users;

    public GetClinicSiteVersionsQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicSite> sites,
        IReadRepository<ClinicSiteVersion> versions,
        IReadRepository<User> users)
    {
        _access = access;
        _sites = sites;
        _versions = versions;
        _users = users;
    }

    public async ValueTask<Result<IReadOnlyList<ClinicSiteVersionDto>>> Handle(GetClinicSiteVersionsQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<IReadOnlyList<ClinicSiteVersionDto>>.Failure(access.Error);
        var tenantId = access.Value;

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
            return Result<IReadOnlyList<ClinicSiteVersionDto>>.Failure(ClinicSiteErrors.NotFound);

        var summaries = await _versions.ListAsync(new ClinicSiteVersionSummariesSpec(tenantId, site.Id), ct);
        var userIds = summaries.Select(s => s.PublishedByUserId).Distinct().ToList();
        var publishers = (await _users.ListAsync(new ClinicSiteVersionPublishersSpec(userIds), ct)).ToDictionary(p => p.Id);

        IReadOnlyList<ClinicSiteVersionDto> items = summaries
            .Select(s => new ClinicSiteVersionDto(
                s.VersionNumber,
                s.VersionNumber == site.PublishedVersionNumber,
                s.PublishedAtUtc,
                s.PublishedByUserId,
                publishers.TryGetValue(s.PublishedByUserId, out var p) ? UserDisplayName.Resolve(p.DisplayName, p.Email) : null))
            .ToList();
        return Result<IReadOnlyList<ClinicSiteVersionDto>>.Success(items);
    }
}

public sealed class SaveClinicSiteDraftCommandHandler : IRequestHandler<SaveClinicSiteDraftCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicSite> _sites;
    private readonly IRepository<ClinicSiteAddress> _addresses;
    private readonly IReadRepository<ClinicSiteImage> _images;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly ClinicSiteAddressBook _addressBook;
    private readonly TimeProvider _timeProvider;

    public SaveClinicSiteDraftCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicSite> sites,
        IRepository<ClinicSiteAddress> addresses,
        IReadRepository<ClinicSiteImage> images,
        IReadRepository<Clinic> clinics,
        ClinicSiteAddressBook addressBook,
        TimeProvider timeProvider)
    {
        _access = access;
        _sites = sites;
        _addresses = addresses;
        _images = images;
        _clinics = clinics;
        _addressBook = addressBook;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(SaveClinicSiteDraftCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(access.Error);
        var tenantId = access.Value;

        var parsed = ClinicSiteConfig.Parse(request.ConfigJson);
        if (!parsed.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(parsed.Error);
        var config = parsed.Value!;

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        var ownership = await EnsureImagesBelongToSiteAsync(tenantId, site?.Id, config, ct);
        if (!ownership.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(ownership.Error);

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        return site is null
            ? await CreateAsync(tenantId, request.ClinicId, config, utcNow, ct)
            : await UpdateAsync(site, config, request.RowVersion, utcNow, ct);
    }

    private async Task<Result<ClinicSiteStateDto>> CreateAsync(Guid tenantId, Guid clinicId, ClinicSiteConfig config, DateTime utcNow, CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, clinicId), ct);
        if (clinic is null)
            return Result<ClinicSiteStateDto>.Failure("Clinics.NotFound", "Klinik bulunamadı.");

        var slug = await _addressBook.SuggestAsync(clinic.Name, ct);
        var site = ClinicSite.Create(tenantId, clinicId, slug, config, utcNow);
        await _sites.AddAsync(site, ct);
        await _addresses.AddAsync(ClinicSiteAddress.Create(tenantId, site.Id, slug, utcNow), ct);

        try
        {
            await _sites.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // İki sekme aynı anda ilk kaydı ya da aynı önerilen adresi yazdı; benzersiz indeks yakalar, istemci yenileyip dener.
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.ConcurrencyConflict);
        }

        return Result<ClinicSiteStateDto>.Success(ClinicSiteMapping.ToState(site));
    }

    private async Task<Result<ClinicSiteStateDto>> UpdateAsync(ClinicSite site, ClinicSiteConfig config, string? rowVersion, DateTime utcNow, CancellationToken ct)
    {
        if (!ExaminationRowVersion.TryDecode(rowVersion, out var expected))
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.InvalidRowVersion);

        site.SaveDraft(config, utcNow);
        site.SetExpectedRowVersion(expected);
        return await ClinicSiteWrites.SaveAsync(_sites, site, ct);
    }

    private async Task<Result> EnsureImagesBelongToSiteAsync(Guid tenantId, Guid? siteId, ClinicSiteConfig config, CancellationToken ct)
    {
        if (config.ImageIds.Count == 0)
            return Result.Success();

        var owned = siteId is { } id
            ? (await _images.ListAsync(new ClinicSiteImageIdsBySiteSpec(tenantId, id), ct)).ToHashSet()
            : [];
        var foreign = config.ImageIds.Where(i => !owned.Contains(i)).ToList();
        return foreign.Count == 0
            ? Result.Success()
            : Result.Failure(ClinicSiteConfig.ValidationError(
                foreign.Select(i => new ClinicSiteConfigIssue(ClinicSiteConfig.ImageReference(i), ClinicSiteConfig.Reasons.ImageNotInSite)).ToList()));
    }
}

public sealed class ChangeClinicSiteAddressCommandHandler : IRequestHandler<ChangeClinicSiteAddressCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicSite> _sites;
    private readonly ClinicSiteAddressBook _addressBook;
    private readonly TimeProvider _timeProvider;

    public ChangeClinicSiteAddressCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicSite> sites,
        ClinicSiteAddressBook addressBook,
        TimeProvider timeProvider)
    {
        _access = access;
        _sites = sites;
        _addressBook = addressBook;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(ChangeClinicSiteAddressCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(access.Error);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicSpec(access.Value, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        var changed = await _addressBook.ChangeAsync(site, request.Slug, _timeProvider.GetUtcNow().UtcDateTime, ct);
        if (!changed.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(changed.Error);

        try
        {
            await _sites.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ClinicSiteErrors.IsSlugViolation(ex))
        {
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.SlugTaken);
        }

        return Result<ClinicSiteStateDto>.Success(ClinicSiteMapping.ToState(site));
    }
}

public sealed class PublishClinicSiteCommandHandler : IRequestHandler<PublishClinicSiteCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ClinicSite> _sites;
    private readonly IRepository<ClinicSiteVersion> _versions;
    private readonly IReadRepository<ClinicSiteImage> _images;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<ClinicWorkingHour> _workingHours;
    private readonly TimeProvider _timeProvider;

    public PublishClinicSiteCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IRepository<ClinicSite> sites,
        IRepository<ClinicSiteVersion> versions,
        IReadRepository<ClinicSiteImage> images,
        IReadRepository<Clinic> clinics,
        IReadRepository<ClinicWorkingHour> workingHours,
        TimeProvider timeProvider)
    {
        _access = access;
        _clientContext = clientContext;
        _sites = sites;
        _versions = versions;
        _images = images;
        _clinics = clinics;
        _workingHours = workingHours;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(PublishClinicSiteCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expected))
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.InvalidRowVersion);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        var requirements = await CheckRequirementsAsync(tenantId, site, ct);
        if (!requirements.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(requirements.Error);

        var published = site.Publish(_clientContext.UserId!.Value, _timeProvider.GetUtcNow().UtcDateTime);
        if (!published.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(published.Error);
        await _versions.AddAsync(published.Value!, ct);

        // Yeni satır henüz kaydedilmedi: mevcutlardan sınırın bir eksiği kalır (K2).
        var beyondLimit = await _versions.ListAsync(
            new ClinicSiteVersionsBeyondLimitSpec(tenantId, site.Id, ClinicSite.MaxRetainedVersions - 1), ct);
        if (beyondLimit.Count > 0)
            await _versions.DeleteRangeAsync(beyondLimit, ct);

        site.SetExpectedRowVersion(expected);
        return await ClinicSiteWrites.SaveAsync(_sites, site, ct);
    }

    private async Task<Result> CheckRequirementsAsync(Guid tenantId, ClinicSite site, CancellationToken ct)
    {
        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, site.ClinicId), ct);
        if (clinic is not { IsActive: true })
            return Result.Failure(ClinicSiteErrors.ClinicInactive);

        var hours = await _workingHours.ListAsync(new ClinicWorkingHoursByClinicSpec(tenantId, site.ClinicId), ct);
        var missing = ClinicSitePublishRequirements.Missing(clinic, hours);

        var draft = ClinicSiteConfig.Parse(site.DraftConfigJson);
        if (!draft.IsSuccess)
            return Result.Failure(draft.Error);

        var images = await _images.ListAsync(new ClinicSiteImagesBySiteSpec(tenantId, site.Id), ct);
        var readyIds = images.Select(i => i.Id).ToHashSet();
        var missingImageIds = draft.Value!.ImageIds.Where(id => !readyIds.Contains(id)).ToList();

        return missing.Count == 0 && missingImageIds.Count == 0
            ? Result.Success()
            : Result.Failure(ClinicSiteErrors.PublishRequirementsNotMet(missing, missingImageIds));
    }
}

public sealed class RestoreClinicSiteVersionCommandHandler : IRequestHandler<RestoreClinicSiteVersionCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicSite> _sites;
    private readonly IReadRepository<ClinicSiteVersion> _versions;
    private readonly TimeProvider _timeProvider;

    public RestoreClinicSiteVersionCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicSite> sites,
        IReadRepository<ClinicSiteVersion> versions,
        TimeProvider timeProvider)
    {
        _access = access;
        _sites = sites;
        _versions = versions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(RestoreClinicSiteVersionCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expected))
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.InvalidRowVersion);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicUntrackedSpec(tenantId, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        var version = await _versions.FirstOrDefaultAsync(new ClinicSiteVersionByNumberSpec(tenantId, site.Id, request.VersionNumber), ct);
        if (version is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.VersionNotFound);

        site.RestoreToDraft(version, _timeProvider.GetUtcNow().UtcDateTime);
        site.SetExpectedRowVersion(expected);
        return await ClinicSiteWrites.SaveAsync(_sites, site, ct);
    }
}

public sealed class CloseClinicSiteCommandHandler : IRequestHandler<CloseClinicSiteCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicSite> _sites;
    private readonly TimeProvider _timeProvider;

    public CloseClinicSiteCommandHandler(ClinicDefinitionAccess access, IRepository<ClinicSite> sites, TimeProvider timeProvider)
    {
        _access = access;
        _sites = sites;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(CloseClinicSiteCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(access.Error);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicSpec(access.Value, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        site.Close(_timeProvider.GetUtcNow().UtcDateTime);
        return await ClinicSiteWrites.SaveTrackedAsync(_sites, site, ct);
    }
}

public sealed class ReopenClinicSiteCommandHandler : IRequestHandler<ReopenClinicSiteCommand, Result<ClinicSiteStateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicSite> _sites;
    private readonly TimeProvider _timeProvider;

    public ReopenClinicSiteCommandHandler(ClinicDefinitionAccess access, IRepository<ClinicSite> sites, TimeProvider timeProvider)
    {
        _access = access;
        _sites = sites;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicSiteStateDto>> Handle(ReopenClinicSiteCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicSiteStateDto>.Failure(access.Error);

        var site = await _sites.FirstOrDefaultAsync(new ClinicSiteByClinicSpec(access.Value, request.ClinicId), ct);
        if (site is null)
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.NotFound);

        site.Reopen(_timeProvider.GetUtcNow().UtcDateTime);
        return await ClinicSiteWrites.SaveTrackedAsync(_sites, site, ct);
    }
}

/// <summary>Site yazımının kayıt ve eşzamanlılık çevirisi tek yerde.</summary>
internal static class ClinicSiteWrites
{
    /// <summary>İzlenmeyen kayıt: beklenen <c>rowVersion</c> ile eklenir, eski sürüm 409 döner.</summary>
    public static async Task<Result<ClinicSiteStateDto>> SaveAsync(IRepository<ClinicSite> sites, ClinicSite site, CancellationToken ct)
    {
        await sites.UpdateAsync(site, ct);
        return await SaveTrackedAsync(sites, site, ct);
    }

    public static async Task<Result<ClinicSiteStateDto>> SaveTrackedAsync(IRepository<ClinicSite> sites, ClinicSite site, CancellationToken ct)
    {
        try
        {
            await sites.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.ConcurrencyConflict);
        }
        catch (DbUpdateException ex) when (ClinicSiteErrors.IsVersionNumberViolation(ex))
        {
            return Result<ClinicSiteStateDto>.Failure(ClinicSiteErrors.ConcurrencyConflict);
        }

        return Result<ClinicSiteStateDto>.Success(ClinicSiteMapping.ToState(site));
    }
}
