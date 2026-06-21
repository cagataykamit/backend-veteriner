namespace Backend.Veteriner.Application.Payments.ReadModels;

/// <summary>
/// Tek günlük klinik+currency bucket parity sapması (PII yok).
/// </summary>
public sealed record PaymentFinanceDailyBucketMismatch(
    Guid TenantId,
    Guid ClinicId,
    DateOnly LocalDate,
    string Currency,
    decimal? CommandTotalAmount,
    int? CommandCount,
    decimal? QueryTotalAmount,
    int? QueryCount);
