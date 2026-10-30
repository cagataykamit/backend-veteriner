using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.Clinics.OnlineBooking;

/// <summary>Kliniğin online randevu ayarları; kayıt yoksa varsayılan (kapalı) döner.</summary>
public sealed record GetClinicOnlineBookingSettingsQuery(Guid ClinicId) : IRequest<Result<ClinicOnlineBookingSettingsDto>>;

/// <summary>Ayarları kaydeder; ilk kayıtta bağlantı kodu üretilir. Ayarlar mevcut klinik ayar yetkisiyle (ADR-042 K4).</summary>
public sealed record UpdateClinicOnlineBookingSettingsCommand(Guid ClinicId, bool IsEnabled, int MinLeadMinutes, int MaxAdvanceDays)
    : IRequest<Result<ClinicOnlineBookingSettingsDto>>, IAuditableRequest
{
    public string AuditAction => "OnlineBooking.Settings.Update";
    public string? AuditTarget => $"ClinicId={ClinicId};IsEnabled={IsEnabled}";
}

/// <summary>Bağlantı kodunu yeniden üretir; eski bağlantı kapanır (ADR-042 K1/K6).</summary>
public sealed record RegenerateClinicBookingCodeCommand(Guid ClinicId)
    : IRequest<Result<ClinicOnlineBookingSettingsDto>>, IAuditableRequest
{
    public string AuditAction => "OnlineBooking.Code.Regenerate";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}

public sealed class GetClinicOnlineBookingSettingsQueryValidator : AbstractValidator<GetClinicOnlineBookingSettingsQuery>
{
    public GetClinicOnlineBookingSettingsQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class UpdateClinicOnlineBookingSettingsCommandValidator : AbstractValidator<UpdateClinicOnlineBookingSettingsCommand>
{
    public UpdateClinicOnlineBookingSettingsCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.MinLeadMinutes).InclusiveBetween(0, ClinicOnlineBookingSettings.MaxLeadMinutes);
        RuleFor(x => x.MaxAdvanceDays).InclusiveBetween(1, ClinicOnlineBookingSettings.MaxAdvanceDaysLimit);
    }
}

public sealed class RegenerateClinicBookingCodeCommandValidator : AbstractValidator<RegenerateClinicBookingCodeCommand>
{
    public RegenerateClinicBookingCodeCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class GetClinicOnlineBookingSettingsQueryHandler
    : IRequestHandler<GetClinicOnlineBookingSettingsQuery, Result<ClinicOnlineBookingSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicOnlineBookingSettings> _settings;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public GetClinicOnlineBookingSettingsQueryHandler(
        ClinicDefinitionAccess access,
        IReadRepository<ClinicOnlineBookingSettings> settings,
        IOptions<OwnerPortalOptions> portal,
        IOptions<AppOptions> app)
    {
        _access = access;
        _settings = settings;
        _portal = portal.Value;
        _app = app.Value;
    }

    public async ValueTask<Result<ClinicOnlineBookingSettingsDto>> Handle(GetClinicOnlineBookingSettingsQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicOnlineBookingSettingsDto>.Failure(access.Error);

        var row = await _settings.FirstOrDefaultAsync(new ClinicOnlineBookingSettingsByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<ClinicOnlineBookingSettingsDto>.Success(ClinicOnlineBookingMapping.ToDto(request.ClinicId, row, _portal, _app));
    }
}

public sealed class UpdateClinicOnlineBookingSettingsCommandHandler
    : IRequestHandler<UpdateClinicOnlineBookingSettingsCommand, Result<ClinicOnlineBookingSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicOnlineBookingSettings> _settings;
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public UpdateClinicOnlineBookingSettingsCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicOnlineBookingSettings> settings,
        OnlineBookingClinicResolver resolver,
        TimeProvider timeProvider,
        IOptions<OwnerPortalOptions> portal,
        IOptions<AppOptions> app)
    {
        _access = access;
        _settings = settings;
        _resolver = resolver;
        _timeProvider = timeProvider;
        _portal = portal.Value;
        _app = app.Value;
    }

    public async ValueTask<Result<ClinicOnlineBookingSettingsDto>> Handle(UpdateClinicOnlineBookingSettingsCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicOnlineBookingSettingsDto>.Failure(access.Error);
        var tenantId = access.Value;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var window = ClinicOnlineBookingSettings.ValidateWindow(request.MinLeadMinutes, request.MaxAdvanceDays);
        if (!window.IsSuccess)
            return Result<ClinicOnlineBookingSettingsDto>.Failure(window.Error);

        var row = await _settings.FirstOrDefaultAsync(new ClinicOnlineBookingSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        if (row is null)
        {
            row = ClinicOnlineBookingSettings.Create(tenantId, request.ClinicId, await _resolver.NewUniqueCodeAsync(ct), utcNow);
            await _settings.AddAsync(row, ct);
        }

        var updated = row.Update(request.IsEnabled, request.MinLeadMinutes, request.MaxAdvanceDays, utcNow);
        if (!updated.IsSuccess)
            return Result<ClinicOnlineBookingSettingsDto>.Failure(updated.Error);

        await _settings.SaveChangesAsync(ct);
        return Result<ClinicOnlineBookingSettingsDto>.Success(ClinicOnlineBookingMapping.ToDto(request.ClinicId, row, _portal, _app));
    }
}

public sealed class RegenerateClinicBookingCodeCommandHandler
    : IRequestHandler<RegenerateClinicBookingCodeCommand, Result<ClinicOnlineBookingSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicOnlineBookingSettings> _settings;
    private readonly OnlineBookingClinicResolver _resolver;
    private readonly TimeProvider _timeProvider;
    private readonly OwnerPortalOptions _portal;
    private readonly AppOptions _app;

    public RegenerateClinicBookingCodeCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicOnlineBookingSettings> settings,
        OnlineBookingClinicResolver resolver,
        TimeProvider timeProvider,
        IOptions<OwnerPortalOptions> portal,
        IOptions<AppOptions> app)
    {
        _access = access;
        _settings = settings;
        _resolver = resolver;
        _timeProvider = timeProvider;
        _portal = portal.Value;
        _app = app.Value;
    }

    public async ValueTask<Result<ClinicOnlineBookingSettingsDto>> Handle(RegenerateClinicBookingCodeCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicOnlineBookingSettingsDto>.Failure(access.Error);
        var tenantId = access.Value;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var code = await _resolver.NewUniqueCodeAsync(ct);

        var row = await _settings.FirstOrDefaultAsync(new ClinicOnlineBookingSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        if (row is null)
        {
            row = ClinicOnlineBookingSettings.Create(tenantId, request.ClinicId, code, utcNow);
            await _settings.AddAsync(row, ct);
        }
        else
        {
            row.RegenerateCode(code, utcNow);
        }

        await _settings.SaveChangesAsync(ct);
        return Result<ClinicOnlineBookingSettingsDto>.Success(ClinicOnlineBookingMapping.ToDto(request.ClinicId, row, _portal, _app));
    }
}
