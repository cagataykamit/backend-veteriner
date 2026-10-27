using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Application.Tenants.Specs;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.OwnerPortal.Invites;

/// <summary>Kabul edilebilir davet ve bağlamı.</summary>
public sealed record ResolvedOwnerPortalInvite(OwnerPortalInvite Invite, Client Client, Clinic Clinic);

/// <summary>
/// Ham davet token'ından kabul edilebilir daveti bulur (önizleme ve kabul aynı kural). Davet bekliyor ve süresi geçmemiş,
/// müşteri hâlâ bu kiracıda, e-postası davet e-postasıyla aynı (K3: e-posta değişince eski davet kapanır) ve hâlâ davet
/// kuralına uyuyor (<see cref="OwnerPortalInvitePolicy"/>), kiracı ve klinik aktif olmalı. Aksi her durumda aynı yanıt:
/// <c>Portal.Invites.NotFound</c>.
/// Davet bağlantısı anonimdir; kiracı davetten çözülür (ADR-040). Token araması kiracılar arasıdır, sonraki sorgular
/// davetin kiracısının kapsamında çalışır.
/// </summary>
public sealed class OwnerPortalInviteResolver
{
    private readonly ITokenHashService _hash;
    private readonly IReadRepository<OwnerPortalInvite> _invites;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Clinic> _clinics;
    private readonly IReadRepository<Tenant> _tenants;

    public OwnerPortalInviteResolver(
        ITokenHashService hash,
        IReadRepository<OwnerPortalInvite> invites,
        ITenantScope tenantScope,
        IReadRepository<Client> clients,
        IReadRepository<Clinic> clinics,
        IReadRepository<Tenant> tenants)
    {
        _hash = hash;
        _invites = invites;
        _tenantScope = tenantScope;
        _clients = clients;
        _clinics = clinics;
        _tenants = tenants;
    }

    public async Task<Result<ResolvedOwnerPortalInvite>> ResolveUsableAsync(string? rawToken, DateTime utcNow, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return NotFound();

        OwnerPortalInvite? invite;
        using (_tenantScope.AsSystem())
            invite = await _invites.FirstOrDefaultAsync(new OwnerPortalInviteByTokenHashSpec(_hash.ComputeSha256(rawToken.Trim())), ct);
        if (invite is null || !invite.IsUsable(utcNow))
            return NotFound();

        // İsteğin geri kalanı (önizleme ya da kabulün yazımları) davetin kiracısına bağlanır; kapsam istekle biter.
        _ = _tenantScope.UseTenant(invite.TenantId);

        var tenant = await _tenants.FirstOrDefaultAsync(new TenantByIdSpec(invite.TenantId), ct);
        if (tenant is not { IsActive: true })
            return NotFound();

        var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(invite.TenantId, invite.ClientId), ct);
        if (client is null
            || !string.Equals(OwnerAccount.NormalizeEmail(client.Email), invite.Email, StringComparison.Ordinal)
            || !OwnerPortalInvitePolicy.CanInvite(client).IsSuccess)
        {
            return NotFound();
        }

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(invite.TenantId, invite.ClinicId), ct);
        if (clinic is not { IsActive: true })
            return NotFound();

        return Result<ResolvedOwnerPortalInvite>.Success(new ResolvedOwnerPortalInvite(invite, client, clinic));
    }

    private static Result<ResolvedOwnerPortalInvite> NotFound()
        => Result<ResolvedOwnerPortalInvite>.Failure("Portal.Invites.NotFound", "Davet geçersiz veya süresi dolmuş.");
}
