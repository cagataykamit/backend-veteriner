using System.Security.Claims;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IJwtTokenService
{
    (string accessToken, string refreshToken, DateTime expiresAt) Create(
        User user,
        IEnumerable<Claim>? extraClaims = null);
}
