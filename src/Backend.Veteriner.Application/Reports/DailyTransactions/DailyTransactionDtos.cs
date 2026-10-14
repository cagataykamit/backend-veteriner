using System.Text.Json.Serialization;
using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Reports.DailyTransactions;

/// <summary>Bir mali olay satırı. <c>Amount</c> pozitiftir, yönü <c>Kind</c> belirler; düzeltme satırında null.</summary>
public sealed record DailyTransactionItemDto(
    DateTime OccurredAtUtc,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DailyTransactionKind Kind,
    Guid ReferenceId,
    Guid ClinicId,
    Guid ClientId,
    string ClientName,
    Guid? PetId,
    string? PetName,
    string? Description,
    decimal? Amount,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod? PaymentMethod,
    bool IsDirectSale,
    bool IsVoided,
    string? Reason,
    Guid? PerformedByUserId,
    string? PerformedByName);

public sealed record DailyTransactionTotalsDto(
    decimal GrossChargeAmount,
    int GrossChargeCount,
    decimal SalesChargeAmount,
    decimal VoidedChargeAmount,
    int VoidedChargeCount,
    decimal NetChargeAmount,
    decimal CollectedAmount,
    int CollectedCount,
    decimal SalesCollectedAmount,
    decimal VoidedCollectionAmount,
    int VoidedCollectionCount,
    decimal NetCollectedAmount,
    int CorrectionCount);

public sealed record DailyTransactionsReportResultDto(
    string Currency,
    DateOnly From,
    DateOnly To,
    DailyTransactionTotalsDto Totals,
    int TotalCount,
    IReadOnlyList<DailyTransactionItemDto> Items);

/// <summary>Kasa özeti tutar kümesi: tahsil, iptal, net (net = tahsil − iptal).</summary>
public record CashSummaryAmountsDto(
    decimal CollectedAmount,
    int CollectedCount,
    decimal VoidedAmount,
    int VoidedCount,
    decimal NetAmount,
    decimal SalesCollectedAmount);

public sealed record CashSummaryMethodDto(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod PaymentMethod,
    decimal CollectedAmount,
    int CollectedCount,
    decimal VoidedAmount,
    int VoidedCount,
    decimal NetAmount,
    decimal SalesCollectedAmount)
    : CashSummaryAmountsDto(CollectedAmount, CollectedCount, VoidedAmount, VoidedCount, NetAmount, SalesCollectedAmount);

public sealed record CashSummaryDayDto(
    DateOnly Date,
    decimal CollectedAmount,
    int CollectedCount,
    decimal VoidedAmount,
    int VoidedCount,
    decimal NetAmount,
    decimal SalesCollectedAmount,
    decimal CashNetAmount,
    decimal CardNetAmount,
    decimal TransferNetAmount)
    : CashSummaryAmountsDto(CollectedAmount, CollectedCount, VoidedAmount, VoidedCount, NetAmount, SalesCollectedAmount);

/// <param name="UserId">Null: işlemi yapan bilinmiyor (tahsisi olmayan eski ödeme).</param>
public sealed record CashSummaryUserDto(
    Guid? UserId,
    string UserName,
    decimal CollectedAmount,
    int CollectedCount,
    decimal VoidedAmount,
    int VoidedCount,
    decimal NetAmount,
    decimal SalesCollectedAmount)
    : CashSummaryAmountsDto(CollectedAmount, CollectedCount, VoidedAmount, VoidedCount, NetAmount, SalesCollectedAmount);

public sealed record CashSummaryVoidedPaymentDto(
    Guid PaymentId,
    DateTime PaidAtUtc,
    DateTime VoidedAtUtc,
    Guid ClientId,
    string ClientName,
    decimal Amount,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PaymentMethod PaymentMethod,
    bool IsDirectSale,
    Guid? CollectedByUserId,
    string? CollectedByName,
    Guid? VoidedByUserId,
    string? VoidedByName,
    string? Reason);

public sealed record CashSummaryReportDto(
    string Currency,
    DateOnly From,
    DateOnly To,
    CashSummaryAmountsDto Totals,
    IReadOnlyList<CashSummaryMethodDto> ByMethod,
    IReadOnlyList<CashSummaryDayDto> ByDay,
    IReadOnlyList<CashSummaryUserDto> ByUser,
    IReadOnlyList<CashSummaryVoidedPaymentDto> VoidedPayments);
