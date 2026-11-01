using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.ClientCredits;

/// <summary>
/// Müşterinin bir klinikteki kredilerinden ücretlere mahsup — tek kural (FIN-005 K3): krediler oluşturma sırasıyla (en eski önce)
/// tüketilir, bir satır birden çok krediden karşılanabilir. Satırlar ve toplam önce doğrulanır; kısmi uygulama olursa
/// çağıran işlem geri alınır (handler <c>ITransactionalRollbackOnFailureRequest</c>).
/// </summary>
public static class ClientCreditLedger
{
    public static Result<IReadOnlyList<ClientCreditApplication>> Apply(
        IReadOnlyList<ClientCredit> credits,
        IReadOnlyList<(Charge Charge, decimal Amount)> lines,
        Guid userId,
        Guid? requestId,
        DateTime nowUtc)
    {
        if (lines.Count == 0)
            return Fail("ClientCredits.AllocationsRequired", "En az bir ücret seçilmelidir.");

        if (lines.Select(l => l.Charge.Id).Distinct().Count() != lines.Count)
            return Fail("ClientCredits.Validation", "Aynı ücret birden fazla kez seçilemez.");

        if (lines.Any(l => l.Amount <= 0 || decimal.Round(l.Amount, 2) != l.Amount))
            return Fail("ClientCredits.Validation", "Mahsup tutarı 0'dan büyük ve en fazla 2 ondalıklı olmalıdır.");

        foreach (var (charge, amount) in lines)
        {
            if (amount > charge.OutstandingAmount)
            {
                return Fail(
                    "PaymentAllocations.ExceedsOutstanding",
                    $"Mahsup tutarı ücretin açık tutarını aşıyor (ChargeId={charge.Id}, açık tutar={charge.OutstandingAmount:0.00}).");
            }
        }

        var ordered = credits.Where(c => c.RemainingAmount > 0).OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).ToList();
        var available = ordered.Sum(c => c.RemainingAmount);
        var requested = lines.Sum(l => l.Amount);
        if (requested > available)
        {
            return Fail(
                "ClientCredits.InsufficientBalance",
                $"Kalan kredi yetmiyor (kalan={available:0.00}, istenen={requested:0.00}).");
        }

        var applications = new List<ClientCreditApplication>();
        var queue = new Queue<ClientCredit>(ordered);
        foreach (var (charge, amount) in lines)
        {
            var left = amount;
            while (left > 0)
            {
                var credit = queue.Peek();
                var portion = Math.Min(left, credit.RemainingAmount);
                var applied = credit.Apply(charge, portion, userId, requestId, nowUtc);
                if (!applied.IsSuccess)
                    return Result<IReadOnlyList<ClientCreditApplication>>.Failure(applied.Error);

                applications.Add(applied.Value!);
                left -= portion;
                if (credit.RemainingAmount == 0)
                    queue.Dequeue();
            }
        }

        return Result<IReadOnlyList<ClientCreditApplication>>.Success(applications);
    }

    private static Result<IReadOnlyList<ClientCreditApplication>> Fail(string code, string message)
        => Result<IReadOnlyList<ClientCreditApplication>>.Failure(code, message);
}
