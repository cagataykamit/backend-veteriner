using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
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
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _opt = options.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_opt.Key) || _opt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key en az 32 karakter olmal�.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    /// <summary>
    /// Kullan�c�ya JWT access & refresh token �retir.
    /// </summary>
    public (string accessToken, string refreshToken, DateTime expiresAt) Create(
        User user,
        IEnumerable<Claim>? extraClaims = null)
    {
        var roleNames = user.Roles.Select(r => r.Name).ToList();
        return Create(user.Id, user.Email, roleNames, extraClaims);
    }

    /// <inheritdoc />
    public (string accessToken, string refreshToken, DateTime expiresAt) Create(
        Guid userId,
        string email,
        IReadOnlyList<string> roleNames,
        IEnumerable<Claim>? extraClaims = null)
    {
        var now = DateTimeOffset.UtcNow;
        var jti = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),

            new(JwtRegisteredClaimNames.Jti, jti),

            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),

            new("nonce", Guid.NewGuid().ToString("N"))
        };

        foreach (var roleName in roleNames)
        {
            claims.Add(new Claim("role", roleName));
            claims.Add(new Claim(ClaimTypes.Role, roleName));
        }

        if (extraClaims is not null)
            claims.AddRange(extraClaims);

        var expires = now.AddMinutes(_opt.ExpMinutes);

        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: _signingCredentials
        );

        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(token);

        var refreshToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

        return (accessToken, refreshToken, expires.UtcDateTime);
    }
}
