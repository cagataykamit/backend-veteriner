using Backend.Veteriner.Domain.OwnerPortal;
using Backend.Veteriner.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Oturum kapatma çakışması "zaten kapatılmış" sayılır (tek yer). Personel ve portal refresh token'ında <c>RevokedAtUtc</c>
/// eşzamanlılık belirtecidir: döndürme (yeni jeton zincire yazılır) çakışırsa istisna kalır, yenileme 401 alır. Yalnız kapatma
/// (çıkış, oturum iptali, tüm oturumları kapatma) çakışırsa satır başka bir istekte zaten kapanmıştır; istisna bastırılır ve
/// kayıt başarılı sayılır, aynı kayıttaki diğer değişiklikler (ör. parola sıfırlama) korunur.
/// </summary>
public sealed class RefreshTokenRevokeConflictInterceptor : SaveChangesInterceptor
{
    private const string RevokedAtUtc = nameof(RefreshToken.RevokedAtUtc);
    private const string ReplacedByTokenHash = nameof(RefreshToken.ReplacedByTokenHash);

    public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result)
        => IsRevokeOnly(eventData.Entries) ? InterceptionResult.Suppress() : result;

    public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
        ConcurrencyExceptionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ThrowingConcurrencyException(eventData, result));

    private static bool IsRevokeOnly(IReadOnlyList<EntityEntry> entries)
        => entries.Count > 0 && entries.All(e =>
            e.Entity is RefreshToken or OwnerPortalRefreshToken
            && e.State == EntityState.Modified
            && e.Property(RevokedAtUtc).CurrentValue is not null
            && !e.Property(ReplacedByTokenHash).IsModified);
}
