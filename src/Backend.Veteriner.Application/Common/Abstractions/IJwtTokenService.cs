using System.Collections.Generic;
using System.Security.Claims;
using Backend.Veteriner.Domain.Users;

namespace Backend.Veteriner.Application.Common.Abstractions;

public interface IJwtTokenService
{
    (string accessToken, string refreshToken, DateTime expiresAt) Create(
        User user,
        IEnumerable<Claim>? extraClaims = null);

    /// <summary>
    /// Login projection path: tam <see cref="User"/> aggregate yüklemeden token üretimi.
    /// </summary>
    (string accessToken, string refreshToken, DateTime expiresAt) Create(
        Guid userId,
        string email,
        IReadOnlyList<string> roleNames,
        IEnumerable<Claim>? extraClaims = null);
}
