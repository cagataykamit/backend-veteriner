using Ardalis.Specification;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;

namespace Backend.Veteriner.Application.Ai;

public sealed class ClinicAiSettingsByClinicSpec : Specification<ClinicAiSettings>
{
    public ClinicAiSettingsByClinicSpec(Guid tenantId, Guid clinicId)
        => Query.Where(s => s.TenantId == tenantId && s.ClinicId == clinicId);
}

/// <summary>Platform yönetimi: klinik kimlikle aranır (sistem kapsamında, kiracı filtresi kapalı).</summary>
public sealed class ClinicForAiAdminSpec : Specification<Clinic>
{
    public ClinicForAiAdminSpec(Guid clinicId) => Query.Where(c => c.Id == clinicId);
}

/// <summary>Platform yönetimi: AI'si açık klinikler.</summary>
public sealed class EnabledClinicAiSettingsSpec : Specification<ClinicAiSettings>
{
    public EnabledClinicAiSettingsSpec() => Query.Where(s => s.IsEnabled);
}

/// <summary>Platform yönetimi: kimliklerle klinikler (sistem kapsamında).</summary>
public sealed class ClinicsForAiAdminSpec : Specification<Clinic>
{
    public ClinicsForAiAdminSpec(IReadOnlyCollection<Guid> clinicIds) => Query.Where(c => clinicIds.Contains(c.Id));
}
