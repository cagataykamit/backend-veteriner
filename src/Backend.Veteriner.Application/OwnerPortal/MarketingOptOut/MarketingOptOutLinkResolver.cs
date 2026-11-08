using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.OwnerPortal.MarketingOptOut;

/// <summary>Bulunan ret bağlantısı, sahibin bağı ve klinik adı. Varlıklar izlenir; ret aynı kayıtta yazılır.</summary>
public sealed record MarketingOptOutScope(MarketingOptOutLink OptOutLink, OwnerAccountClientLink ClientLink, string ClinicName);

/// <summary>
/// Ret bağlantısının kapısı — tek yer (ADR-046 K3). Ham token özetiyle kiracılar arası aranır, isteğin geri kalanı bağlantının
/// kiracısına bağlanır (ADR-040). Ret her zaman çalışır: kapanmış bağ, pasif kiracı, salt okunur abonelik ya da kapalı ticari ileti
/// kapısı reddi engellemez. Bilinmeyen token için sebep ayırt edilmeden <see cref="NotFoundCode"/> (404).
/// </summary>
public sealed class MarketingOptOutLinkResolver
{
    public const string NotFoundCode = "MarketingOptOut.NotFound";

    /// <summary>Üretilen token 43 karakterdir; çok uzun girdi özetlenmeden reddedilir.</summary>
    private const int MaxTokenLength = 128;

    private readonly ITokenHashService _hash;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<MarketingOptOutLink> _optOutLinks;
    private readonly IReadRepository<OwnerAccountClientLink> _clientLinks;
    private readonly IReadRepository<Clinic> _clinics;

    public MarketingOptOutLinkResolver(
        ITokenHashService hash,
        ITenantScope tenantScope,
        IReadRepository<MarketingOptOutLink> optOutLinks,
        IReadRepository<OwnerAccountClientLink> clientLinks,
        IReadRepository<Clinic> clinics)
    {
        _hash = hash;
        _tenantScope = tenantScope;
        _optOutLinks = optOutLinks;
        _clientLinks = clientLinks;
        _clinics = clinics;
    }

    public async Task<Result<MarketingOptOutScope>> ResolveAsync(string? rawToken, CancellationToken ct)
    {
        var token = rawToken?.Trim();
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return NotFound();

        MarketingOptOutLink? optOut;
        using (_tenantScope.AsSystem())
            optOut = await _optOutLinks.FirstOrDefaultAsync(new MarketingOptOutLinkByTokenHashSpec(_hash.ComputeSha256(token)), ct);
        if (optOut is null)
            return NotFound();

        // İsteğin geri kalanı bağlantının kiracısına bağlanır; kapsam istekle biter.
        _ = _tenantScope.UseTenant(optOut.TenantId);

        var clientLink = await _clientLinks.FirstOrDefaultAsync(new OwnerAccountClientLinkByIdSpec(optOut.OwnerAccountClientLinkId), ct);
        if (clientLink is null)
            return NotFound();

        var clinic = await _clinics.FirstOrDefaultAsync(new ClinicByIdSpec(optOut.TenantId, optOut.ClinicId), ct);
        return Result<MarketingOptOutScope>.Success(new MarketingOptOutScope(optOut, clientLink, clinic?.Name ?? string.Empty));
    }

    private static Result<MarketingOptOutScope> NotFound()
        => Result<MarketingOptOutScope>.Failure(NotFoundCode, "Bağlantı geçersiz.");
}

public sealed class MarketingOptOutLinkByTokenHashSpec : Specification<MarketingOptOutLink>
{
    public MarketingOptOutLinkByTokenHashSpec(string tokenHash)
        => Query.Where(x => x.TokenHash == tokenHash);
}
