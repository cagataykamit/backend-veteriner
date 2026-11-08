using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Reminders.Marketing;

/// <summary>
/// Kliniğin ticari ileti kapısı (ADR-046 K2; docs/AUTOMATED_REMINDERS_API_CONTRACT.md §2). <see cref="DeclarationText"/> panelde
/// onaylanan beyandır; <see cref="DeclaredAtUtc"/> ve <see cref="DeclaredByUserId"/> son açılıştaki beyanı gösterir.
/// </summary>
public sealed record ClinicMarketingGateDto(
    bool IsEnabled,
    string DeclarationText,
    string DeclarationVersion,
    DateTime? DeclaredAtUtc,
    Guid? DeclaredByUserId,
    DateTime? UpdatedAtUtc)
{
    public static ClinicMarketingGateDto From(ClinicMarketingSettings? row)
        => new(
            ClinicMarketingSettings.IsOpen(row),
            ClinicMarketingSettings.DeclarationText,
            ClinicMarketingSettings.CurrentDeclarationVersion,
            row?.DeclaredAtUtc,
            row?.DeclaredByUserId,
            row?.UpdatedAtUtc);
}

public sealed class ClinicMarketingSettingsByClinicSpec : Specification<ClinicMarketingSettings>
{
    public ClinicMarketingSettingsByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId);
}

/// <summary>Kliniğin ticari ileti kapısı açık mı — uygulama katmanındaki tek okuma yeri; kayıt yoksa kapalı.</summary>
public sealed class ClinicMarketingGate
{
    private readonly IReadRepository<ClinicMarketingSettings> _settings;

    public ClinicMarketingGate(IReadRepository<ClinicMarketingSettings> settings) => _settings = settings;

    public async Task<ClinicMarketingSettings?> GetAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
        => await _settings.FirstOrDefaultAsync(new ClinicMarketingSettingsByClinicSpec(tenantId, clinicId), ct);

    public async Task<bool> IsOpenAsync(Guid tenantId, Guid clinicId, CancellationToken ct)
        => ClinicMarketingSettings.IsOpen(await GetAsync(tenantId, clinicId, ct));
}
