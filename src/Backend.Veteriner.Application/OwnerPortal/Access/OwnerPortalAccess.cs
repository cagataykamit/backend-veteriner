using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.OwnerPortal.Access;

/// <summary>Bağ kapsamlı portal ucunun çalışacağı kiracı/klinik/müşteri; sorgular yalnız bu filtreyle çalışır.</summary>
public sealed record OwnerPortalLinkScope(Guid LinkId, Guid OwnerAccountId, Guid TenantId, Guid ClinicId, Guid ClientId);

/// <summary>
/// Hesabın kullanabildiği bağ ve görünen adları. <c>IsPortalServiceAvailable</c> <c>false</c>: kiracının paketinde portal yok
/// (ADR-035 2026-11-08); okuma kapalı, sahip hakları (veri talebi, tercihler) açık. Sebep (paket) sahibe söylenmez.
/// </summary>
public sealed record OwnerPortalActiveLink(
    OwnerAccountClientLink Link, string ClinicName, string ClientFullName, bool IsClinicPortalEnabled = true, bool IsPortalServiceAvailable = true);

/// <summary>
/// Portal sahiplik kontrolü — tek yer (ADR-031 K6). Bağ hesabın, aktif ve kiracısı aktif olmalıdır. Aksi her durum
/// <c>Portal.Links.NotFound</c> (404): başka hesabın, kapalı ya da başka kiracının bağı birbirinden ayırt edilmez.
/// Dilim B okuma uçları <see cref="ResolveLinkAsync"/> ile başlar.
/// Portal JWT'sinde kiracı yoktur; kiracı bağdan çözülür (ADR-040). Bağ araması hesaba göre kiracılar arasıdır,
/// sonraki sorgular bağın kiracısının kapsamında çalışır.
/// </summary>
public sealed class OwnerPortalAccess
{
    private readonly IReadRepository<OwnerAccount> _accounts;
    private readonly IReadRepository<OwnerAccountClientLink> _links;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<Client> _clients;
    private readonly ClinicPortalGate _clinicPortal;
    private readonly TenantPlanFeatureEvaluator _planFeatures;

    public OwnerPortalAccess(
        IReadRepository<OwnerAccount> accounts,
        IReadRepository<OwnerAccountClientLink> links,
        IReadRepository<Tenant> tenants,
        IReadRepository<Clinic> clinics,
        ITenantScope tenantScope,
        IReadRepository<Client> clients,
        ClinicPortalGate clinicPortal,
        TenantPlanFeatureEvaluator planFeatures)
    {
        _clinicPortal = clinicPortal;
        _planFeatures = planFeatures;
        _accounts = accounts;
        _links = links;
        _tenants = tenants;
        _clinics = clinics;
        _tenantScope = tenantScope;
        _clients = clients;
    }

    /// <summary>Oturumdaki hesap hâlâ aktif mi; değilse portal uçları 401 döner.</summary>
    public async Task<Result<OwnerAccount>> GetActiveAccountAsync(Guid ownerAccountId, CancellationToken ct)
    {
        var account = await _accounts.FirstOrDefaultAsync(new OwnerAccountByIdSpec(ownerAccountId), ct);
        return account is { IsActive: true }
            ? Result<OwnerAccount>.Success(account)
            : Result<OwnerAccount>.Failure(OwnerPortalErrors.AccountUnavailable);
    }

    /// <summary>
    /// Okuma ve hizmet uçları (hayvan, aşı, randevu, bakiye, klinik bilgisi, portaldan randevu talebi): kiracının paketinde
    /// <see cref="PlanFeature.OwnerPortal"/> yoksa kapanır (ADR-035 2026-11-08); yanıt sahiplik kuralıyla aynı 404'tür.
    /// </summary>
    public Task<Result<OwnerPortalLinkScope>> ResolveLinkAsync(Guid ownerAccountId, Guid linkId, CancellationToken ct)
        => ResolveAsync(ownerAccountId, linkId, requirePortalPlan: true, ct);

    /// <summary>
    /// Sahip hakları (KVKK veri talebi, aydınlatma metni, bildirim ve hatırlatma tercihleri): paket portal içermese de açık kalır,
    /// sahip mevcut bağı üzerinden haklarını kullanabilir. Hesap silme bağdan bağımsızdır.
    /// </summary>
    public Task<Result<OwnerPortalLinkScope>> ResolveLinkForOwnerRightsAsync(Guid ownerAccountId, Guid linkId, CancellationToken ct)
        => ResolveAsync(ownerAccountId, linkId, requirePortalPlan: false, ct);

    private async Task<Result<OwnerPortalLinkScope>> ResolveAsync(Guid ownerAccountId, Guid linkId, bool requirePortalPlan, CancellationToken ct)
    {
        var account = await GetActiveAccountAsync(ownerAccountId, ct);
        if (!account.IsSuccess)
            return Result<OwnerPortalLinkScope>.Failure(account.Error);

        OwnerAccountClientLink? link;
        using (_tenantScope.AsSystem())
            link = await _links.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(linkId), ct);
        if (link is null || link.OwnerAccountId != ownerAccountId || !link.IsActive)
            return LinkNotFound();

        var tenants = await _tenants.ListAsync(new PortalTenantsByIdsSpec([link.TenantId]), ct);
        if (tenants is not [{ IsActive: true }])
            return LinkNotFound();

        // İsteğin geri kalanı bağın kiracısına bağlanır; kapsam DI kapsamıyla (istekle) biter.
        _ = _tenantScope.UseTenant(link.TenantId);

        // Klinik portalı kapattıysa (PORTAL-009) bağ kapsamlı uçlar kapanır; yanıt sahiplik kuralıyla aynıdır.
        if (!await _clinicPortal.IsEnabledAsync(link.TenantId, link.ClinicId, ct))
            return LinkNotFound();

        if (requirePortalPlan && !await _planFeatures.IsAllowedAsync(link.TenantId, PlanFeature.OwnerPortal, ct))
            return LinkNotFound();

        return Result<OwnerPortalLinkScope>.Success(
            new OwnerPortalLinkScope(link.Id, link.OwnerAccountId, link.TenantId, link.ClinicId, link.ClientId));
    }

    /// <summary>Hesabın kullanabildiği aktif bağlar (aktif kiracılarda), klinik ve müşteri adıyla.</summary>
    public async Task<IReadOnlyList<OwnerPortalActiveLink>> ListActiveLinksAsync(Guid ownerAccountId, CancellationToken ct)
    {
        // Hesabın bağları birden çok kiracıya yayılabilir; klinik ve müşteri adları yalnız bu bağların kimlikleriyle okunur.
        using var system = _tenantScope.AsSystem();
        var links = await _links.ListAsync(new ActiveOwnerAccountClientLinksByAccountSpec(ownerAccountId), ct);
        if (links.Count == 0)
            return [];

        var activeTenantIds = (await _tenants.ListAsync(new PortalTenantsByIdsSpec(links.Select(l => l.TenantId).Distinct().ToArray()), ct))
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToHashSet();
        var usable = links.Where(l => activeTenantIds.Contains(l.TenantId)).ToArray();
        if (usable.Length == 0)
            return [];

        var clinicNames = (await _clinics.ListAsync(new PortalClinicsByIdsSpec(usable.Select(l => l.ClinicId).Distinct().ToArray()), ct))
            .ToDictionary(c => c.Id, c => c.Name);
        var clientNames = (await _clients.ListAsync(new PortalClientsByIdsSpec(usable.Select(l => l.ClientId).Distinct().ToArray()), ct))
            .ToDictionary(c => c.Id, c => c.FullName);
        var disabledClinics = await _clinicPortal.DisabledClinicIdsAsync(usable.Select(l => l.ClinicId).Distinct().ToArray(), ct);
        var portalTenants = new HashSet<Guid>();
        foreach (var tenantId in usable.Select(l => l.TenantId).Distinct())
        {
            if (await _planFeatures.IsAllowedAsync(tenantId, PlanFeature.OwnerPortal, ct))
                portalTenants.Add(tenantId);
        }

        return usable
            .Select(l => new OwnerPortalActiveLink(
                l,
                clinicNames.GetValueOrDefault(l.ClinicId, string.Empty),
                clientNames.GetValueOrDefault(l.ClientId, string.Empty),
                !disabledClinics.Contains(l.ClinicId),
                portalTenants.Contains(l.TenantId)))
            .ToArray();
    }

    private static Result<OwnerPortalLinkScope> LinkNotFound()
        => Result<OwnerPortalLinkScope>.Failure("Portal.Links.NotFound", "Kayıt bulunamadı.");
}
