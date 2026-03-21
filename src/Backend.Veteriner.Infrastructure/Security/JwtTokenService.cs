using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Veteriner.Infrastructure.Security;

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _opt;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _opt = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_opt.Key) || _opt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key en az 32 karakter olmal�.");
    }

    /// <summary>
    /// Kullan�c�ya JWT access & refresh token �retir.
    /// </summary>
    public (string accessToken, string refreshToken, DateTime expiresAt) Create(
        User user,
        IEnumerable<Claim>? extraClaims = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var now = DateTimeOffset.UtcNow;
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),

            // benzersiz id
            new(JwtRegisteredClaimNames.Jti, jti),

            // issued-at (epoch, integer)
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),

            // ekstra benzersiz de�er (tam farkl�l�k i�in)
            new("nonce", Guid.NewGuid().ToString("N"))
        };

        // rolleri hem standard "role" hem de ClaimTypes.Role olarak yaz
        foreach (var role in user.Roles)
        {
            claims.Add(new Claim("role", role.Name));               // JWT standard
            claims.Add(new Claim(ClaimTypes.Role, role.Name));      // .NET uyumluluk
        }

        // ?? Ekstra claim'leri (permissions vb.) dahil et
        if (extraClaims is not null)
            claims.AddRange(extraClaims);

        var expires = now.AddMinutes(_opt.ExpMinutes);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds
        );

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(token);

        // basit refresh token (ileride persiste edip revoke edece�iz)
        var refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        return (accessToken, refreshToken, expires.UtcDateTime);
    }
}
