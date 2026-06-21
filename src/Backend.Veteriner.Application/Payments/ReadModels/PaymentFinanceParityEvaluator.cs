namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Saf, deterministik payment finance parity değerlendirmesi.
/// Canlı okuma <see cref="IPaymentFinanceParityReader"/>'da yapılır.
/// </summary>
public static class PaymentFinanceParityEvaluator
{
    public sealed record DailyBucketSnapshot(
        Guid TenantId,
        Guid ClinicId,
        DateOnly LocalDate,
        string Currency,
        decimal TotalAmount,
        int Count);

    public static PaymentFinanceParityResult Evaluate(
        long commandPaymentCount,
        long queryContributionCount,
        IReadOnlyList<DailyBucketSnapshot> commandBuckets,
        IReadOnlyList<DailyBucketSnapshot> queryBuckets,
        Guid? scopeTenantId = null)
    {
        var commandMap = commandBuckets.ToDictionary(
            b => (b.TenantId, b.ClinicId, b.LocalDate, b.Currency),
            b => b);
        var queryMap = queryBuckets.ToDictionary(
            b => (b.TenantId, b.ClinicId, b.LocalDate, b.Currency),
            b => b);

        var allKeys = commandMap.Keys.Union(queryMap.Keys).ToHashSet();
        var mismatches = new List<PaymentFinanceDailyBucketMismatch>();

        foreach (var key in allKeys)
        {
            commandMap.TryGetValue(key, out var commandBucket);
            queryMap.TryGetValue(key, out var queryBucket);

            var commandTotal = commandBucket?.TotalAmount;
            var commandCount = commandBucket?.Count;
            var queryTotal = queryBucket?.TotalAmount;
            var queryCount = queryBucket?.Count;

            if (commandTotal == queryTotal && commandCount == queryCount)
                continue;

            mismatches.Add(new PaymentFinanceDailyBucketMismatch(
                key.TenantId,
                key.ClinicId,
                key.LocalDate,
                key.Currency,
                commandTotal,
                commandCount,
                queryTotal,
                queryCount));
        }

        var countInSync = commandPaymentCount == queryContributionCount;
        var dailyInSync = mismatches.Count == 0;

        return new PaymentFinanceParityResult(
            commandPaymentCount,
            queryContributionCount,
            countInSync,
            dailyInSync,
            mismatches.Count,
            mismatches,
            scopeTenantId);
    }
}
