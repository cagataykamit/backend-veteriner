using Backend.Veteriner.Application.Reports.DailyTransactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Persistence.Repositories.Reports;

/// <summary>
/// Günlük işlem dökümü ve kasa özeti okuyucuları. İkisi de <see cref="FinanceEvents"/> kaynağını kullanır: döküm toplamı tek gruplu sorgu,
/// sayfa SQL'de sıralanıp kesilir; kasa özeti yalnız tahsilat olaylarını yükler (kırılımlar uygulama katmanında aynı satırlardan).
/// </summary>
public sealed class DailyTransactionsReader : IDailyTransactionsReader, ICashEventsReader
{
    private static readonly DailyTransactionKind[] AllKinds = Enum.GetValues<DailyTransactionKind>();

    private static readonly DailyTransactionKind[] CashKinds = [DailyTransactionKind.Payment, DailyTransactionKind.PaymentVoid];

    private readonly AppDbContext _db;
    private readonly ILogger<DailyTransactionsReader> _logger;

    public DailyTransactionsReader(AppDbContext db, ILogger<DailyTransactionsReader> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DailyTransactionsReadResult> ReadAsync(FinanceEventFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var events = FinanceEvents.Query(_db, filter, AllKinds);

        var groups = await events
            .GroupBy(e => new { e.Kind, e.IsDirectSale })
            .Select(g => new { g.Key.Kind, g.Key.IsDirectSale, Count = g.Count(), Sum = g.Sum(e => e.Amount) ?? 0m })
            .ToListAsync(ct);

        var totals = BuildTotals(groups.Select(g => (g.Kind, g.IsDirectSale, g.Count, g.Sum)));
        var totalCount = groups.Sum(g => g.Count);
        if (totalCount == 0 || skip >= totalCount)
            return new DailyTransactionsReadResult(totalCount, totals, []);

        var page = await events
            .OrderBy(e => e.AtUtc).ThenBy(e => e.Kind).ThenBy(e => e.ReferenceId)
            .Skip(skip).Take(take)
            .ToListAsync(ct);

        var names = await FinanceEvents.LoadNamesAsync(_db, page, ct);
        var items = page.Select(e => new DailyTransactionItemDto(
            e.AtUtc,
            e.Kind,
            e.ReferenceId,
            e.ClinicId,
            e.ClientId,
            names.Client(e.ClientId),
            e.PetId,
            names.Pet(e.PetId),
            e.Description,
            e.Amount,
            e.Method,
            e.IsDirectSale,
            e.IsVoided,
            FinanceEvents.ReasonOf(e, _logger),
            e.PerformedByUserId,
            names.User(e.PerformedByUserId))).ToList();

        return new DailyTransactionsReadResult(totalCount, totals, items);
    }

    public async Task<IReadOnlyList<CashEventRow>> ReadAsync(FinanceEventFilter filter, int maxRows, CancellationToken ct = default)
    {
        var rows = await FinanceEvents.Query(_db, filter, CashKinds)
            .OrderBy(e => e.AtUtc).ThenBy(e => e.Kind).ThenBy(e => e.ReferenceId)
            .Take(maxRows + 1)
            .ToListAsync(ct);

        var names = await FinanceEvents.LoadNamesAsync(_db, rows, ct);
        return rows.Select(e => new CashEventRow(
            e.Kind == DailyTransactionKind.PaymentVoid,
            e.AtUtc,
            e.ReferenceId,
            e.PaidAtUtc ?? e.AtUtc,
            e.ClientId,
            names.Client(e.ClientId),
            e.Amount ?? 0m,
            e.Method!.Value,
            e.IsDirectSale,
            e.CollectedByUserId,
            names.User(e.CollectedByUserId),
            e.PerformedByUserId,
            names.User(e.PerformedByUserId),
            e.Reason)).ToList();
    }

    private static DailyTransactionTotalsDto BuildTotals(IEnumerable<(DailyTransactionKind Kind, bool IsDirectSale, int Count, decimal Sum)> groups)
    {
        var list = groups.ToList();
        int Count(DailyTransactionKind k) => list.Where(g => g.Kind == k).Sum(g => g.Count);
        decimal Amount(DailyTransactionKind k, bool? sale = null) =>
            list.Where(g => g.Kind == k && (sale is null || g.IsDirectSale == sale)).Sum(g => g.Sum);

        var grossCharge = Amount(DailyTransactionKind.Charge);
        var voidedCharge = Amount(DailyTransactionKind.ChargeVoid);
        var collected = Amount(DailyTransactionKind.Payment);
        var voidedCollection = Amount(DailyTransactionKind.PaymentVoid);

        return new DailyTransactionTotalsDto(
            grossCharge,
            Count(DailyTransactionKind.Charge),
            Amount(DailyTransactionKind.Charge, sale: true),
            voidedCharge,
            Count(DailyTransactionKind.ChargeVoid),
            grossCharge - voidedCharge,
            collected,
            Count(DailyTransactionKind.Payment),
            Amount(DailyTransactionKind.Payment, sale: true),
            voidedCollection,
            Count(DailyTransactionKind.PaymentVoid),
            collected - voidedCollection,
            Count(DailyTransactionKind.ChargeCorrection) + Count(DailyTransactionKind.PaymentCorrection));
    }
}
