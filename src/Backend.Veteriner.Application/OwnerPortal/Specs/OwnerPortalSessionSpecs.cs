using Ardalis.Specification;
using Backend.Veteriner.Domain.OwnerPortal;

namespace Backend.Veteriner.Application.OwnerPortal.Specs;

/// <summary>Kullanılmamış ve geçersiz kılınmamış, süresi geçmemiş kodlar (izlenir; yeni kod bunları geçersiz kılar).</summary>
public sealed class OpenOwnerPortalCodesSpec : Specification<OwnerPortalCode>
{
    public OpenOwnerPortalCodesSpec(Guid ownerAccountId, OwnerPortalCodePurpose purpose, DateTime utcNow)
    {
        Query.Where(x => x.OwnerAccountId == ownerAccountId
                         && x.Purpose == purpose
                         && x.ConsumedAtUtc == null
                         && x.InvalidatedAtUtc == null
                         && x.ExpiresAtUtc > utcNow);
    }
}

/// <summary>Amaç için en son üretilen kod (izlenir; doğrulama denemesi yazılır).</summary>
public sealed class LatestOwnerPortalCodeSpec : Specification<OwnerPortalCode>
{
    public LatestOwnerPortalCodeSpec(Guid ownerAccountId, OwnerPortalCodePurpose purpose)
    {
        Query.Where(x => x.OwnerAccountId == ownerAccountId && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(1);
    }
}

public sealed class OwnerPortalRefreshTokenByHashSpec
    : Specification<OwnerPortalRefreshToken>
{
    public OwnerPortalRefreshTokenByHashSpec(string tokenHash)
    {
        Query.Where(x => x.TokenHash == tokenHash);
    }
}

/// <summary>Hesabın iptal edilmemiş oturumları (izlenir; toplu çıkış/sıfırlama/tekrar kullanım tespitinde kapatılır).</summary>
public sealed class OpenOwnerPortalRefreshTokensByAccountSpec : Specification<OwnerPortalRefreshToken>
{
    public OpenOwnerPortalRefreshTokensByAccountSpec(Guid ownerAccountId)
    {
        Query.Where(x => x.OwnerAccountId == ownerAccountId && x.RevokedAtUtc == null);
    }
}
