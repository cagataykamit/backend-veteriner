using Ardalis.Specification;
using Backend.Veteriner.Application.Appointments.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.Appointments.OwnerLinks;

/// <summary>Kullanılabilir bağlantı ve bağlamı. Varlıklar izlenir; eylem uçları aynı kayıtta yazar.</summary>
public sealed record AppointmentOwnerLinkScope(
    AppointmentOwnerLink Link,
    Appointment Appointment,
    Clinic Clinic,
    string PetName,
    bool CancelLinkEnabled);

/// <summary>
/// Hatırlatma bağlantısının kapısı — tek yer (TIER-019). Ham token özetiyle kiracılar arası aranır, isteğin geri kalanı
/// bağlantının kiracısına bağlanır (ADR-040). Bilinmeyen, kapanmış, süresi dolmuş token; yeniden zamanlanmış, iptal ya da
/// tamamlanmış randevu; pasif kiracı/klinik ya da salt okunur abonelik — hepsinde sebep ayırt edilmeden
/// <see cref="NotFoundCode"/> (404).
/// </summary>
public sealed class AppointmentOwnerLinkResolver
{
    public const string NotFoundCode = "AppointmentLinks.NotFound";

    /// <summary>Üretilen token 43 karakterdir; çok uzun girdi özetlenmeden reddedilir.</summary>
    private const int MaxTokenLength = 128;

    private readonly ITokenHashService _hash;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<AppointmentOwnerLink> _links;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Pet> _pets;
    private readonly IReadRepository<ClinicAppointmentSettings> _settings;
    private readonly TenantSubscriptionEffectiveWriteEvaluator _subscription;

    public AppointmentOwnerLinkResolver(
        ITokenHashService hash,
        ITenantScope tenantScope,
        IReadRepository<AppointmentOwnerLink> links,
        IReadRepository<Tenant> tenants,
        IReadRepository<Appointment> appointments,
        IReadRepository<Clinic> clinics,
        IReadRepository<Pet> pets,
        IReadRepository<ClinicAppointmentSettings> settings,
        TenantSubscriptionEffectiveWriteEvaluator subscription)
    {
        _hash = hash;
        _tenantScope = tenantScope;
        _links = links;
        _tenants = tenants;
        _appointments = appointments;
        _clinics = clinics;
        _pets = pets;
        _settings = settings;
        _subscription = subscription;
    }

    public async Task<Result<AppointmentOwnerLinkScope>> ResolveAsync(string? rawToken, DateTime utcNow, CancellationToken ct)
    {
        var token = rawToken?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return NotFound();

        AppointmentOwnerLink? link;
        using (_tenantScope.AsSystem())
            link = await _links.FirstOrDefaultAsync(new AppointmentOwnerLinkByTokenHashSpec(_hash.ComputeSha256(token)), ct);
        if (link is null || link.ClosedAtUtc is not null)
            return NotFound();

        // İsteğin geri kalanı bağlantının kiracısına bağlanır; kapsam istekle biter.
        _ = _tenantScope.UseTenant(link.TenantId);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(link.TenantId), ct);
        if (tenant is not { IsActive: true } || !(await _subscription.EnsureWriteAllowedAsync(link.TenantId, ct)).IsSuccess)
            return NotFound();

        var appointment = await _appointments.FirstOrDefaultAsync(new AppointmentByIdSpec(link.TenantId, link.AppointmentId), ct);
        if (appointment is null || !link.IsUsableFor(appointment, utcNow))
            return NotFound();

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(link.TenantId, appointment.ClinicId), ct);
        if (clinic is not { IsActive: true })
            return NotFound();

        var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(link.TenantId, appointment.PetId), ct);
        var settings = await _settings.FirstOrDefaultAsync(new ClinicAppointmentSettingsByClinicSpec(link.TenantId, clinic.Id), ct);

        return Result<AppointmentOwnerLinkScope>.Success(new AppointmentOwnerLinkScope(
            link,
            appointment,
            clinic,
            pet?.Name ?? string.Empty,
            settings?.OwnerCancelLinkEnabled ?? ClinicAppointmentSettings.DefaultOwnerCancelLinkEnabled));
    }

    private static Result<AppointmentOwnerLinkScope> NotFound()
        => Result<AppointmentOwnerLinkScope>.Failure(NotFoundCode, "Bağlantı geçersiz ya da süresi dolmuş.");
}

public sealed class AppointmentOwnerLinkByTokenHashSpec : Specification<AppointmentOwnerLink>
{
    public AppointmentOwnerLinkByTokenHashSpec(string tokenHash)
        => Query.Where(x => x.TokenHash == tokenHash);
}

/// <summary>Randevunun açık bağlantıları (izlenir; bir eylem hepsini kapatır).</summary>
public sealed class OpenAppointmentOwnerLinksByAppointmentSpec : Specification<AppointmentOwnerLink>
{
    public OpenAppointmentOwnerLinksByAppointmentSpec(Guid tenantId, Guid appointmentId)
        => Query.Where(x => x.TenantId == tenantId && x.AppointmentId == appointmentId && x.ClosedAtUtc == null);
}
