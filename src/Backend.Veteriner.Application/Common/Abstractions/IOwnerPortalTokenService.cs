using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.Common.Abstractions;

/// <summary>
/// Hasta sahibi portalı access token'ı (ADR-031 K6): personelden ayrı hedef kitle, <c>principal_type=owner</c>;
/// izin, kiracı ve klinik claim'i taşımaz.
/// </summary>
public interface IOwnerPortalTokenService
{
    (string AccessToken, DateTime ExpiresAtUtc) CreateAccessToken(OwnerAccount account);
}
