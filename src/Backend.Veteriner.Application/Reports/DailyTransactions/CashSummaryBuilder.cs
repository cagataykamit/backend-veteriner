using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Payments;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.DailyTransactions;

/// <summary>
/// Kasa özetinin tek hesap yeri: aynı tahsilat/iptal olay kümesinden yöntem, gün, kullanıcı kırılımlarını ve toplamı türetir
/// (hepsi aynı satırlardan geldiği için birbiriyle tutarlıdır). Sözleşme bölüm 4, K-D6-2 ve K-D6-7.
/// </summary>
internal static class CashSummaryBuilder
{
    private static readonly PaymentMethod[] Methods = [PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.Transfer];

    public static readonly RegisterExportSpec<CashSummaryExportRow> ExportSpec = new(
    [
        RegisterColumn<CashSummaryExportRow>.Text(null, "Bölüm", r => r.Section),
        RegisterColumn<CashSummaryExportRow>.Text(null, "Ad", r => r.Name),
        RegisterColumn<CashSummaryExportRow>.Number(null, "Tahsilat Adedi", r => r.CollectedCount),
        RegisterColumn<CashSummaryExportRow>.Money(null, "Tahsilat Tutarı", r => r.CollectedAmount),
        RegisterColumn<CashSummaryExportRow>.Number(null, "İptal Adedi", r => r.VoidedCount),
        RegisterColumn<CashSummaryExportRow>.Money(null, "İptal Tutarı", r => r.VoidedAmount),
        RegisterColumn<CashSummaryExportRow>.Money(null, "Net", r => r.Net),
    ],
    "Kasa Ozeti",
    "kasa-ozeti");

    public static CashSummaryReportDto Build(DateOnly from, DateOnly to, IReadOnlyList<CashEventRow> rows)
    {
        var byMethod = Methods
            .Select(m =>
            {
                var a = Sum(rows.Where(r => r.PaymentMethod == m));
                return new CashSummaryMethodDto(m, a.CollectedAmount, a.CollectedCount, a.VoidedAmount, a.VoidedCount, a.NetAmount, a.SalesCollectedAmount);
            })
            .ToList();

        var byDay = rows
            .GroupBy(r => OperationDayBounds.ToLocalDate(r.OccurredAtUtc))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var a = Sum(g);
                return new CashSummaryDayDto(
                    g.Key, a.CollectedAmount, a.CollectedCount, a.VoidedAmount, a.VoidedCount, a.NetAmount, a.SalesCollectedAmount,
                    Sum(g.Where(r => r.PaymentMethod == PaymentMethod.Cash)).NetAmount,
                    Sum(g.Where(r => r.PaymentMethod == PaymentMethod.Card)).NetAmount,
                    Sum(g.Where(r => r.PaymentMethod == PaymentMethod.Transfer)).NetAmount);
            })
            .ToList();

        // Tahsilat tahsilatı alana, iptal iptal edene yazılır (K-D6-7).
        var byUser = rows
            .GroupBy(r => r.Voided ? r.PerformedByUserId : r.CollectedByUserId)
            .Select(g =>
            {
                var a = Sum(g);
                var name = g.Select(r => r.Voided ? r.PerformedByName : r.CollectedByName).FirstOrDefault(n => n is not null);
                return new CashSummaryUserDto(
                    g.Key, name ?? "Bilinmiyor", a.CollectedAmount, a.CollectedCount, a.VoidedAmount, a.VoidedCount, a.NetAmount, a.SalesCollectedAmount);
            })
            .OrderByDescending(u => u.NetAmount)
            .ThenBy(u => u.UserName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var voided = rows
            .Where(r => r.Voided)
            .OrderBy(r => r.OccurredAtUtc).ThenBy(r => r.PaymentId)
            .Select(r => new CashSummaryVoidedPaymentDto(
                r.PaymentId, r.PaidAtUtc, r.OccurredAtUtc, r.ClientId, r.ClientName, r.Amount, r.PaymentMethod, r.IsDirectSale,
                r.CollectedByUserId, r.CollectedByName, r.PerformedByUserId, r.PerformedByName, r.Reason))
            .ToList();

        return new CashSummaryReportDto(Charge.DefaultCurrency, from, to, Sum(rows), byMethod, byDay, byUser, voided);
    }

    /// <summary>Dosya satırları: Toplam, Ödeme Türü, Gün, Kullanıcı bölümleri (ekranla aynı veriden).</summary>
    public static IReadOnlyList<CashSummaryExportRow> ToExportRows(CashSummaryReportDto report)
    {
        var rows = new List<CashSummaryExportRow> { Row("Toplam", "Toplam", report.Totals) };
        rows.AddRange(report.ByMethod.Select(m => Row("Ödeme Türü", PaymentMethodTurkishDisplay.ToLabel(m.PaymentMethod), m)));
        rows.AddRange(report.ByDay.Select(d => Row("Gün", d.Date.ToString("dd.MM.yyyy", RegisterDisplay.Turkish), d)));
        rows.AddRange(report.ByUser.Select(u => Row("Kullanıcı", u.UserName, u)));
        return rows;

        static CashSummaryExportRow Row(string section, string name, CashSummaryAmountsDto a)
            => new(section, name, a.CollectedCount, a.CollectedAmount, a.VoidedCount, a.VoidedAmount, a.NetAmount);
    }

    private static CashSummaryAmountsDto Sum(IEnumerable<CashEventRow> rows)
    {
        var list = rows.ToList();
        var collected = list.Where(r => !r.Voided).ToList();
        var voided = list.Where(r => r.Voided).ToList();
        var collectedAmount = collected.Sum(r => r.Amount);
        var voidedAmount = voided.Sum(r => r.Amount);
        return new CashSummaryAmountsDto(
            collectedAmount,
            collected.Count,
            voidedAmount,
            voided.Count,
            collectedAmount - voidedAmount,
            collected.Where(r => r.IsDirectSale).Sum(r => r.Amount));
    }
}

public sealed record CashSummaryExportRow(
    string Section,
    string Name,
    int CollectedCount,
    decimal CollectedAmount,
    int VoidedCount,
    decimal VoidedAmount,
    decimal Net);
