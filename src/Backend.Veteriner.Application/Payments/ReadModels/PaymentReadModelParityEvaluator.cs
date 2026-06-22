namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Saf, deterministik payment list read-model parity değerlendirmesi (CQRS-14F).
/// Canlı okuma <see cref="IPaymentReadModelParityReader"/>'da yapılır; bu sınıf yalnızca karar mantığıdır.
/// </summary>
public static class PaymentReadModelParityEvaluator
{
    /// <summary>
    /// Parity karşılaştırması için tek bir ödeme satırının kritik alanları (Command ve Query tarafında ortak şekil).
    /// </summary>
    public sealed record RowSnapshot(
        Guid PaymentId,
        Guid ClientId,
        Guid? PetId,
        decimal Amount,
        string Currency,
        int Method,
        DateTime PaidAtUtc,
        string ClientName,
        string? PetName,
        string? Notes,
        string ClinicName = "");

    public static PaymentReadModelParityResult Evaluate(
        long commandCount,
        long queryCount,
        IReadOnlyList<RowSnapshot> commandRecent,
        IReadOnlyList<RowSnapshot> queryRecent,
        Guid tenantId,
        Guid clinicId)
    {
        var countInSync = commandCount == queryCount;

        // Recent ordering parity: PaidAtUtc DESC, PaymentId DESC sıralı PaymentId dizisi aynı olmalı.
        var recentOrderingInSync = commandRecent
            .Select(r => r.PaymentId)
            .SequenceEqual(queryRecent.Select(r => r.PaymentId));

        // Row sample parity: recent örnekteki ortak PaymentId'ler için kritik alan karşılaştırması.
        var mismatches = new List<PaymentReadModelRowMismatch>();
        var queryMap = queryRecent.ToDictionary(r => r.PaymentId);
        var commandMap = commandRecent.ToDictionary(r => r.PaymentId);

        foreach (var command in commandRecent)
        {
            if (!queryMap.TryGetValue(command.PaymentId, out var query))
            {
                mismatches.Add(new PaymentReadModelRowMismatch(command.PaymentId, "missing-in-query"));
                continue;
            }

            var field = FirstFieldDifference(command, query);
            if (field is not null)
                mismatches.Add(new PaymentReadModelRowMismatch(command.PaymentId, field));
        }

        foreach (var query in queryRecent)
        {
            if (!commandMap.ContainsKey(query.PaymentId))
                mismatches.Add(new PaymentReadModelRowMismatch(query.PaymentId, "missing-in-command"));
        }

        var rowSampleInSync = mismatches.Count == 0;

        return new PaymentReadModelParityResult(
            commandCount,
            queryCount,
            countInSync,
            rowSampleInSync,
            mismatches.Count,
            mismatches,
            recentOrderingInSync,
            tenantId,
            clinicId);
    }

    private static string? FirstFieldDifference(RowSnapshot command, RowSnapshot query)
    {
        if (command.Amount != query.Amount)
            return nameof(RowSnapshot.Amount);
        if (!string.Equals(command.Currency, query.Currency, StringComparison.Ordinal))
            return nameof(RowSnapshot.Currency);
        if (command.Method != query.Method)
            return nameof(RowSnapshot.Method);
        if (command.PaidAtUtc != query.PaidAtUtc)
            return nameof(RowSnapshot.PaidAtUtc);
        if (command.ClientId != query.ClientId)
            return nameof(RowSnapshot.ClientId);
        if (command.PetId != query.PetId)
            return nameof(RowSnapshot.PetId);
        if (!string.Equals(command.ClientName, query.ClientName, StringComparison.Ordinal))
            return nameof(RowSnapshot.ClientName);
        if (!string.Equals(command.ClinicName, query.ClinicName, StringComparison.Ordinal))
            return nameof(RowSnapshot.ClinicName);
        if (!string.Equals(command.PetName, query.PetName, StringComparison.Ordinal))
            return nameof(RowSnapshot.PetName);
        if (!string.Equals(command.Notes, query.Notes, StringComparison.Ordinal))
            return nameof(RowSnapshot.Notes);

        return null;
    }
}
