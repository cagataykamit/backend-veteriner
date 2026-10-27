using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Persistence;

/// <summary>
/// Handler commit'inin tek kuralı (ADR-039): <see cref="EfUnitOfWork"/> ve <c>EfRepository</c> buradan kaydeder.
/// Kayıt başarısızsa (benzersizlik, eşzamanlılık) iş birimi düşer ve izlenen değişiklikler atılır; böylece
/// istisnayı yakalayıp yoluna devam eden handler başarısız varlığı sonraki bir kayıtta yeniden yazmaz.
/// </summary>
internal static class UnitOfWorkCommit
{
    internal static async Task<int> CommitAsync(this AppDbContext db, CancellationToken ct)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
