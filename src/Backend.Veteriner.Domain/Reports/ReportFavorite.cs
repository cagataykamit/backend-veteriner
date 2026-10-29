using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Reports;

/// <summary>
/// Kullanıcının Rapor Merkezi'nde favoriye eklediği rapor (REPORT-003). Kiracı + kullanıcı + rapor anahtarı benzersizdir;
/// kayıt yalnız eklenir ve silinir, değişmez.
/// </summary>
public sealed class ReportFavorite : ITenantOwned
{
    /// <summary>Kullanıcı başına favori tavanı (katalogdaki rapor sayısının üstünde güvenlik sınırı).</summary>
    public const int MaxPerUser = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string ReportKey { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    private ReportFavorite() { }

    public static ReportFavorite Create(Guid tenantId, Guid userId, string reportKey, DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));

        return new ReportFavorite
        {
            TenantId = tenantId,
            UserId = userId,
            ReportKey = Reports.ReportKey.Ensure(reportKey, nameof(reportKey)),
            CreatedAtUtc = utcNow,
        };
    }
}
