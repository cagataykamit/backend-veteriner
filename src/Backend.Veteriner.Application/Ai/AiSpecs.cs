using Ardalis.Specification;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Tenants;

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

/// <summary>Platform yönetimi: AI ayarı olan (bir kez açılmış ya da kapatılmış) tüm klinikler.</summary>
public sealed class AllClinicAiSettingsSpec : Specification<ClinicAiSettings>
{
    public AllClinicAiSettingsSpec() => Query.AsNoTracking();
}

/// <summary>Platform yönetimi: kimliklerle kliniklerin AI ayarları.</summary>
public sealed class ClinicAiSettingsByClinicIdsSpec : Specification<ClinicAiSettings>
{
    public ClinicAiSettingsByClinicIdsSpec(IReadOnlyCollection<Guid> clinicIds)
    {
        Query.AsNoTracking();
        Query.Where(s => clinicIds.Contains(s.ClinicId));
    }
}

/// <summary>Platform yönetimi: kimliklerle klinikler (sistem kapsamında).</summary>
public sealed class ClinicsForAiAdminSpec : Specification<Clinic>
{
    public ClinicsForAiAdminSpec(IReadOnlyCollection<Guid> clinicIds) => Query.Where(c => clinicIds.Contains(c.Id));
}

/// <summary>Platform yönetimi: adı ya da kiracısı aramaya uyan klinikler (ada göre, en çok <paramref name="take"/>).</summary>
public sealed class ClinicsForAiAdminSearchSpec : Specification<Clinic>
{
    public ClinicsForAiAdminSearchSpec(string search, IReadOnlyCollection<Guid> tenantIds, int take)
    {
        Query.AsNoTracking();
        Query.Where(c => c.Name.Contains(search) || tenantIds.Contains(c.TenantId));
        Query.OrderBy(c => c.Name).Take(take);
    }
}

/// <summary>Platform yönetimi: adı aramaya uyan kiracılar.</summary>
public sealed class TenantsForAiAdminSearchSpec : Specification<Tenant>
{
    public TenantsForAiAdminSearchSpec(string search, int take)
    {
        Query.AsNoTracking();
        Query.Where(t => t.Name.Contains(search)).Take(take);
    }
}

/// <summary>Platform yönetimi: kimliklerle kiracılar.</summary>
public sealed class TenantsForAiAdminSpec : Specification<Tenant>
{
    public TenantsForAiAdminSpec(IReadOnlyCollection<Guid> tenantIds)
    {
        Query.AsNoTracking();
        Query.Where(t => tenantIds.Contains(t.Id));
    }
}

/// <summary>Platform yönetimi: kiracıların abonelikleri (paket hediyesi için).</summary>
public sealed class TenantSubscriptionsForAiAdminSpec : Specification<TenantSubscription>
{
    public TenantSubscriptionsForAiAdminSpec(IReadOnlyCollection<Guid> tenantIds)
    {
        Query.AsNoTracking();
        Query.Where(s => tenantIds.Contains(s.TenantId));
    }
}

/// <summary>Kliniğin fon hareketleri, yeniden eskiye.</summary>
public sealed class AiCreditGrantsByClinicSpec : Specification<AiCreditGrant>
{
    public AiCreditGrantsByClinicSpec(Guid clinicId, int take)
    {
        Query.AsNoTracking();
        Query.Where(g => g.ClinicId == clinicId).OrderByDescending(g => g.CreatedAtUtc).Take(take);
    }
}
