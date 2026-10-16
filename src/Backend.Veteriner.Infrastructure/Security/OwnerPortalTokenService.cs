using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Options;
using Backend.Veteriner.Application.OwnerPortal;
using Backend.Veteriner.Domain.OwnerPortal;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Veteriner.Infrastructure.Security;

/// <summary>
/// Portal access token'ı: personelle aynı imza anahtarı ve yayıncı, <b>ayrı hedef kitle</b> (<see cref="OwnerPortalOptions.Audience"/>)
/// ve <c>principal_type=owner</c>. Personel şeması bu token'ı hedef kitle uyuşmazlığıyla reddeder (ADR-031 K6).
/// </summary>
public sealed class OwnerPortalTokenService : IOwnerPortalTokenService
{
    private readonly JwtOptions _jwt;
    private readonly OwnerPortalOptions _portal;
    private readonly SigningCredentials _signingCredentials;

    public OwnerPortalTokenService(IOptions<JwtOptions> jwt, IOptions<OwnerPortalOptions> portal)
    {
        _jwt = jwt.Value ?? throw new ArgumentNullException(nameof(jwt));
        _portal = portal.Value ?? throw new ArgumentNullException(nameof(portal));
        if (string.IsNullOrWhiteSpace(_jwt.Key) || _jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key en az 32 karakter olmalı.");
        if (string.Equals(_portal.Audience, _jwt.Audience, StringComparison.Ordinal))
            throw new InvalidOperationException("OwnerPortal:Audience, Jwt:Audience ile aynı olamaz.");

        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
            SecurityAlgorithms.HmacSha256);
    }

    public (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(OwnerAccount account)
    {
        // JwtBearer doğrulaması sistem saatini kullanır; süre de ondan hesaplanır (personel JwtTokenService ile aynı).
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_portal.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString("D")),
            new(JwtRegisteredClaimNames.Email, account.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(OwnerPortalAuth.PrincipalTypeClaim, OwnerPortalAuth.OwnerPrincipalType),
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _portal.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
