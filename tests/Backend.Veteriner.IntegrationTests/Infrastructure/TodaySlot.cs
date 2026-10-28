using Backend.Veteriner.Application.Common.Time;

namespace Backend.IntegrationTests.Infrastructure;

/// <summary>
/// "Bugün" testlerinde göreli randevu saati. Bugün ucu planlı randevuyu İstanbul günü içinde ise (saati geçmiş olsa da)
/// listeler; <c>UtcNow + 30 dk</c> gibi bir saat ise 23:30'dan sonra ertesi güne düşer ve test koşu saatine bağlı kırılırdı.
/// Gün bitmeye <see cref="MaxOffset"/>'ten az kaldıysa çapa günün son saatlerine çekilir; farkların sırası korunur.
/// Geçmiş saat gereken testler (ör. gelmedi işareti) <see cref="BeforeNow"/> kullanır: gece yarısından hemen sonra dünkü güne düşmez.
/// </summary>
public static class TodaySlot
{
    /// <summary>Testlerde kullanılan en büyük fark.</summary>
    public static readonly TimeSpan MaxOffset = TimeSpan.FromHours(2);

    public static DateTime FromNow(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero || offset > MaxOffset)
            throw new ArgumentOutOfRangeException(nameof(offset), $"Fark 0 ile {MaxOffset} arasında olmalı.");

        var now = DateTime.UtcNow;
        var dayEnd = OperationDayBounds.ForLocalDate(OperationDayBounds.ToLocalDate(now)).DayEndUtc;
        var anchor = now + MaxOffset < dayEnd ? now : dayEnd - MaxOffset;
        return anchor + offset;
    }

    /// <summary>Şimdiden <paramref name="offset"/> önce; gün başından geriye gitmez (gün yeni başladıysa gün başı).</summary>
    public static DateTime BeforeNow(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(offset), "Fark pozitif olmalı.");

        var now = DateTime.UtcNow;
        var dayStart = OperationDayBounds.ForLocalDate(OperationDayBounds.ToLocalDate(now)).DayStartUtc;
        var at = now - offset;
        return at < dayStart ? dayStart : at;
    }
}
