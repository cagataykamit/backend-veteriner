using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Clinics.OnlineBooking;

/// <summary>Online talep akışının çalışacağı klinik ve ayarları; sorgular bu kiracının kapsamında çalışır.</summary>
public sealed record OnlineBookingScope(Guid TenantId, Guid ClinicId, Clinic Clinic, ClinicOnlineBookingSettings Settings);

/// <summary>
/// Online randevu kapısı — tek yer (ADR-042 K1/K6). Kimliksiz bağlantıda klinik koddan kiracılar arası çözülür ve isteğin geri
/// kalanı o kiracıya bağlanır (ADR-040). Kod bilinmiyor, online randevu kapalı, klinik ya da kiracı pasifse sebep ayırt
/// edilmeden <c>OnlineBooking.NotFound</c> (404). Aboneliği salt okunur kiracı talep alamaz (onaylayamayacağı talep birikmez).
/// Portal akışı aynı kapıyı bağın kliniğiyle kullanır.
/// </summary>
public sealed class OnlineBookingClinicResolver
{
    public const string NotFoundCode = "OnlineBooking.NotFound";
    public const string NotFoundMessage = "Online randevu sayfası bulunamadı.";

    private readonly IReadRepository<ClinicOnlineBookingSettings> _settings;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly ITenantScope _tenantScope;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _subscription;

    public OnlineBookingClinicResolver(
        IReadRepository<ClinicOnlineBookingSettings> settings,
        IReadRepository<Clinic> clinics,
        IReadRepository<Tenant> tenants,
        ITenantScope tenantScope,
        IOwnerPortalSecretGenerator secrets,
        TenantSubscriptionEffectiveWriteEvaluator subscription)
    {
        _settings = settings;
        _clinics = clinics;
        _tenants = tenants;
        _tenantScope = tenantScope;
        _secrets = secrets;
        _subscription = subscription;
    }

    /// <summary>Kimliksiz bağlantı: kod → klinik. Başarıda istek kiracısı bağlanır.</summary>
    public async Task<Result<OnlineBookingScope>> ResolveByCodeAsync(string? bookingCode, CancellationToken ct)
    {
        var code = bookingCode?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code) || code.Length != ClinicOnlineBookingSettings.BookingCodeLength)
            return NotFound();

        ClinicOnlineBookingSettings? settings;
        using (_tenantScope.AsSystem())
            settings = await _settings.FirstOrDefaultAsync(new ClinicOnlineBookingSettingsByCodeSpec(code), ct);
        if (settings is null)
            return NotFound();

        var tenants = await _tenants.ListAsync(new PortalTenantsByIdsSpec([settings.TenantId]), ct);
        if (tenants is not [{ IsActive: true }])
            return NotFound();

        // İsteğin geri kalanı kliniğin kiracısına bağlanır; kapsam DI kapsamıyla (istekle) biter.
        _ = _tenantScope.UseTenant(settings.TenantId);
        return await ResolveEnabledAsync(settings, ct);
    }

    /// <summary>Portal: bağın kliniği (kiracı bağ çözümünde zaten bağlanmıştır).</summary>
    public async Task<Result<OnlineBookingScope>> ResolveByClinicAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
    {
        var settings = await _settings.FirstOrDefaultAsync(new ClinicOnlineBookingSettingsByClinicSpec(tenantId, clinicId), ct);
        return settings is null ? NotFound() : await ResolveEnabledAsync(settings, ct);
    }

    /// <summary>Kod üretimi: tahmin edilemez, karışmayan küçük harf/rakam; çakışma olasılığı ihmal edilebilir, yine de denetlenir.</summary>
    public async Task<string> NewUniqueCodeAsync(CancellationToken ct)
    {
        using var system = _tenantScope.AsSystem();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = _secrets.CreateLinkCode(ClinicOnlineBookingSettings.BookingCodeLength);
            if (!await _settings.AnyAsync(new ClinicOnlineBookingSettingsByCodeSpec(code), ct))
                return code;
        }

        throw new InvalidOperationException("Benzersiz bağlantı kodu üretilemedi.");
    }

    private async Task<Result<OnlineBookingScope>> ResolveEnabledAsync(ClinicOnlineBookingSettings settings, CancellationToken ct)
    {
        if (!settings.IsEnabled)
            return NotFound();

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(settings.TenantId, settings.ClinicId), ct);
        if (clinic is not { IsActive: true })
            return NotFound();

        if (!(await _subscription.EnsureWriteAllowedAsync(settings.TenantId, ct)).IsSuccess)
            return NotFound();

        return Result<OnlineBookingScope>.Success(new OnlineBookingScope(settings.TenantId, clinic.Id, clinic, settings));
    }

    private static Result<OnlineBookingScope> NotFound()
        => Result<OnlineBookingScope>.Failure(NotFoundCode, NotFoundMessage);
}
