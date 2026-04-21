namespace Backend.Veteriner.Application.Dashboard.Contracts.Dtos;

/// <summary>
/// Dashboard mini-trend (sparkline) satırı: bir İstanbul takvim günü için aggregate para toplamı.
/// <para>
/// <c>date</c> alanı <see cref="DateOnly"/>'dır (JSON'da <c>yyyy-MM-dd</c>) ve **Europe/Istanbul** yerel gününü temsil eder.
/// Seri <see cref="DashboardFinanceSummaryDto.Last7DaysPaid"/> içinde **en eskiden en yeniye** (ASC) sıralıdır.
/// </para>
/// <para>
/// <b>Mixed-currency uyarısı:</b> Mevcut finance summary çizgisi gibi bu trend alanı da farklı para birimli ödemeleri
/// ayırmadan aynı <c>decimal</c> toplamına ekler (bkz. §27.6). Tek kiracıda birden çok currency kullanılıyorsa
/// günlük toplam yanıltıcı olabilir; kırılım ileri fazda (backlog) değerlendirilir.
/// </para>
/// </summary>
public sealed record DashboardDailyTotalDto(DateOnly Date, decimal TotalAmount);
