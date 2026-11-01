using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Access;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.PortalSettings;

/// <summary>Kliniğin portal ayarı ve etkisi (açık bağ, bekleyen davet sayısı); docs/PORTAL_API_CONTRACT.md §12.</summary>
public sealed record ClinicPortalSettingsDto(
    Guid ClinicId,
    bool IsEnabled,
    DateTime? UpdatedAtUtc,
    int ActiveLinkCount,
    int PendingInviteCount);

/// <summary>Kliniğin portal ayarı; kayıt yoksa açık (PORTAL-009).</summary>
public sealed record GetClinicPortalSettingsQuery(Guid ClinicId) : IRequest<Result<ClinicPortalSettingsDto>>;

/// <summary>
/// Kliniğin portalını açar/kapatır (ADR-031 K8 çıkış planı). Kapatınca hesaplar ve bağlar silinmez; yeni davet gitmez, bağ kapsamlı
/// portal uçları kapanır. Ayar mevcut klinik ayar yetkisiyle (<c>Clinics.Update</c>).
/// </summary>
public sealed record UpdateClinicPortalSettingsCommand(Guid ClinicId, bool IsEnabled)
    : IRequest<Result<ClinicPortalSettingsDto>>, IAuditableRequest
{
    public string AuditAction => "OwnerPortal.ClinicSettings.Update";
    public string? AuditTarget => $"ClinicId={ClinicId};IsEnabled={IsEnabled}";
}

public sealed class GetClinicPortalSettingsQueryValidator : AbstractValidator<GetClinicPortalSettingsQuery>
{
    public GetClinicPortalSettingsQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class UpdateClinicPortalSettingsCommandValidator : AbstractValidator<UpdateClinicPortalSettingsCommand>
{
    public UpdateClinicPortalSettingsCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class ActiveOwnerAccountClientLinksByClinicSpec : Specification<OwnerAccountClientLink>
{
    public ActiveOwnerAccountClientLinksByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(l => l.TenantId == tenantId && l.ClinicId == clinicId && l.Status == OwnerAccountClientLinkStatus.Active);
}

public sealed class PendingOwnerPortalInvitesByClinicSpec : Specification<OwnerPortalInvite>
{
    public PendingOwnerPortalInvitesByClinicSpec(Guid tenantId, Guid clinicId, DateTime utcNow)
        => Query.Where(i => i.TenantId == tenantId && i.ClinicId == clinicId
                            && i.Status == OwnerPortalInviteStatus.Pending && i.ExpiresAtUtc > utcNow);
}

internal sealed class ClinicPortalSettingsReader
{
    private readonly IReadRepository<OwnerAccountClientLink> _links;
    private readonly IReadRepository<OwnerPortalInvite> _invites;
    private readonly TimeProvider _timeProvider;

    public ClinicPortalSettingsReader(
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<OwnerPortalInvite> invites,
        TimeProvider timeProvider)
    {
        _links = links;
        _invites = invites;
        _timeProvider = timeProvider;
    }

    public async Task<ClinicPortalSettingsDto> ToDtoAsync(Guid tenantId, Guid clinicId, ClinicPortalSettings? row, CancellationToken ct)
        => new(
            clinicId,
            ClinicPortalSettings.IsEnabledOrDefault(row),
            row?.UpdatedAtUtc,
            await _links.CountAsync(new ActiveOwnerAccountClientLinksByClinicSpec(tenantId, clinicId), ct),
            await _invites.CountAsync(new PendingOwnerPortalInvitesByClinicSpec(tenantId, clinicId, _timeProvider.GetUtcNow().UtcDateTime), ct));
}

public sealed class GetClinicPortalSettingsQueryHandler : IRequestHandler<GetClinicPortalSettingsQuery, Result<ClinicPortalSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicPortalSettings> _settings;
    private readonly ClinicPortalSettingsReader _reader;

    public GetClinicPortalSettingsQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicPortalSettings> settings,
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<OwnerPortalInvite> invites,
        TimeProvider timeProvider)
    {
        _access = access;
        _settings = settings;
        _reader = new ClinicPortalSettingsReader(links, invites, timeProvider);
    }

    public async ValueTask<Result<ClinicPortalSettingsDto>> Handle(GetClinicPortalSettingsQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicPortalSettingsDto>.Failure(access.Error);

        var row = await _settings.FirstOrDefaultAsync(new ClinicPortalSettingsByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<ClinicPortalSettingsDto>.Success(await _reader.ToDtoAsync(access.Value, request.ClinicId, row, ct));
    }
}

public sealed class UpdateClinicPortalSettingsCommandHandler : IRequestHandler<UpdateClinicPortalSettingsCommand, Result<ClinicPortalSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ClinicPortalSettings> _settings;
    private readonly ClinicPortalSettingsReader _reader;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicPortalSettingsCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IRepository<ClinicPortalSettings> settings,
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<OwnerPortalInvite> invites,
        TimeProvider timeProvider)
    {
        _access = access;
        _clientContext = clientContext;
        _settings = settings;
        _reader = new ClinicPortalSettingsReader(links, invites, timeProvider);
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicPortalSettingsDto>> Handle(UpdateClinicPortalSettingsCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicPortalSettingsDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (_clientContext.UserId is not { } userId)
            return Result<ClinicPortalSettingsDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var row = await _settings.FirstOrDefaultAsync(new ClinicPortalSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        if (row is null)
        {
            row = ClinicPortalSettings.Create(tenantId, request.ClinicId, utcNow);
            await _settings.AddAsync(row, ct);
        }

        row.SetEnabled(request.IsEnabled, userId, utcNow);
        await _settings.SaveChangesAsync(ct);
        return Result<ClinicPortalSettingsDto>.Success(await _reader.ToDtoAsync(tenantId, request.ClinicId, row, ct));
    }
}
