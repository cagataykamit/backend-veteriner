using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal.Contracts;
using Backend.Veteriner.Application.OwnerPortal.Specs;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Application.OwnerPortal.Sessions;

/// <summary>
/// Portal oturumu üretir: access token + ayrı tablodaki refresh (ham değer yalnız yanıtta). Davet kabulü, kodla/parolayla
/// giriş ve yenileme aynı yolu kullanır. Kayıt refresh deposuna eklemeyle kalıcılaşır; o an izlenen diğer
/// değişiklikler (hesap sayaçları, eski refresh'in döndürülmesi) aynı kayıtta yazılır.
/// </summary>
public sealed class OwnerPortalSessionIssuer
{
    private readonly IOwnerPortalTokenService _tokens;
    private readonly IOwnerPortalSecretGenerator _secrets;
    private readonly ITokenHashService _hash;
    private readonly IRepository<OwnerPortalRefreshToken> _refreshTokens;
    private readonly OwnerPortalOptions _options;

    public OwnerPortalSessionIssuer(
        IOwnerPortalTokenService tokens,
        IOwnerPortalSecretGenerator secrets,
        ITokenHashService hash,
        IRepository<OwnerPortalRefreshToken> refreshTokens,
        IOptions<OwnerPortalOptions> options)
    {
        _tokens = tokens;
        _secrets = secrets;
        _hash = hash;
        _refreshTokens = refreshTokens;
        _options = options.Value;
    }

    /// <param name="replacing">Yenilemede döndürülen eski refresh; yeni özet zincire yazılır.</param>
    public async Task<PortalAuthTokensDto> IssueAsync(
        OwnerAccount account,
        DateTime utcNow,
        CancellationToken ct,
        OwnerPortalRefreshToken? replacing = null)
    {
        var (accessToken, accessExpiresAtUtc) = _tokens.CreateAccessToken(account);

        var rawRefresh = _secrets.CreateToken();
        var refreshHash = _hash.ComputeSha256(rawRefresh);
        replacing?.ReplaceWith(refreshHash, utcNow);

        var refresh = OwnerPortalRefreshToken.Create(account.Id, refreshHash, utcNow.AddDays(_options.RefreshTokenDays), utcNow);
        await _refreshTokens.AddAsync(refresh, ct);

        return new PortalAuthTokensDto(accessToken, accessExpiresAtUtc, rawRefresh, refresh.ExpiresAtUtc);
    }

    /// <summary>Hesabın açık tüm oturumlarını kapatır (kaydı çağıran yapar).</summary>
    public async Task RevokeAllAsync(Guid ownerAccountId, string reason, DateTime utcNow, CancellationToken ct)
    {
        foreach (var token in await _refreshTokens.ListAsync(new OpenOwnerPortalRefreshTokensByAccountSpec(ownerAccountId), ct))
            token.Revoke(reason, utcNow);
    }
}
