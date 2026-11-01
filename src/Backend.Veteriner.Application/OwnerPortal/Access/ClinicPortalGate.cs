using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.OwnerPortal.Access;

/// <summary>
/// Klinik düzeyinde portal açık mı — tek yer (PORTAL-009; ADR-031 K8). Kayıt yoksa açık. Davet oluşturma, davet önizleme/kabul,
/// bağ kapsamlı portal uçları (<see cref="OwnerPortalAccess"/>) ve <c>/me</c> bunu kullanır.
/// </summary>
public sealed class ClinicPortalGate
{
    public const string DisabledCode = "OwnerPortal.ClinicPortalDisabled";

    private readonly IReadRepository<ClinicPortalSettings> _settings;

    public ClinicPortalGate(IReadRepository<ClinicPortalSettings> settings) => _settings = settings;

    public async Task<bool> IsEnabledAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
        => ClinicPortalSettings.IsEnabledOrDefault(
            await _settings.FirstOrDefaultAsync(new ClinicPortalSettingsByClinicSpec(tenantId, clinicId), ct));

    /// <summary>Verilen kliniklerden portalı kapalı olanlar (kiracılar arası çağrıda çağıran <c>AsSystem</c> kapsamındadır).</summary>
    public async Task<IReadOnlySet<Guid>> DisabledClinicIdsAsync(IReadOnlyCollection<Guid> clinicIds, CancellationToken ct)
        => clinicIds.Count == 0
            ? new HashSet<Guid>()
            : (await _settings.ListAsync(new DisabledClinicPortalSettingsSpec(clinicIds), ct)).Select(s => s.ClinicId).ToHashSet();
}

public sealed class ClinicPortalSettingsByClinicSpec : Specification<ClinicPortalSettings>
{
    public ClinicPortalSettingsByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId);
}

public sealed class DisabledClinicPortalSettingsSpec : Specification<ClinicPortalSettings>
{
    public DisabledClinicPortalSettingsSpec(IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(s => clinicIds.Contains(s.ClinicId) && !s.IsEnabled);
    }
}
