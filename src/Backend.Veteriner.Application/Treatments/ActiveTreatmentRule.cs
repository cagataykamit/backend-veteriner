using System.Linq.Expressions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Treatments;

namespace Backend.Veteriner.Application.Treatments;

/// <summary>
/// "Aktif tedavi" (ADR-025 madde 2): takip tarihi klinik yerel günüyle (Europe/Istanbul) bugün veya sonrası olan tedavi.
/// Takip tarihi olmayan tedavi aktif sayılmaz; takip günü geçince kendiliğinden aktiflikten düşer. Tanım yalnızca burada yaşar;
/// liste süzgeci ve sayaçlar (<c>GET /treatments?active=true</c>, <c>totalCount</c>) bunu kullanır.
/// </summary>
public static class ActiveTreatmentRule
{
    /// <summary>Aktiflik sınırı: <paramref name="utcNow"/> anındaki İstanbul gününün başlangıcı (UTC).</summary>
    public static DateTime FollowUpFromUtc(DateTime utcNow) => OperationDayBounds.ForUtcNow(utcNow).DayStartUtc;

    public static Expression<Func<Treatment, bool>> IsActive(DateTime followUpFromUtc)
        => t => t.FollowUpDateUtc != null && t.FollowUpDateUtc >= followUpFromUtc;
}
