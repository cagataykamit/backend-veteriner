using Backend.Veteriner.Domain.Payments;

namespace Backend.Veteriner.Application.Clients.Contracts.Dtos;

/// <summary>
/// GET /clients/{id}/payment-summary. <see cref="TotalPaidAmount"/> tek para birimi olduğunda o birimin toplamıdır; aksi halde 0 — çoklu birim için <see cref="CurrencyTotals"/> esas alınır.
/// </summary>
public sealed record ClientPaymentSummaryDto(
    Guid ClientId,
    string ClientName,
    int TotalPaymentsCount,
    decimal TotalPaidAmount,
    IReadOnlyList<ClientPaymentCurrencyTotalDto> CurrencyTotals,
    DateTime? LastPaymentAtUtc,
    IReadOnlyList<ClientPaymentRecentItemDto> RecentPayments);

public sealed record ClientPaymentCurrencyTotalDto(string Currency, decimal TotalAmount);

public sealed record ClientPaymentRecentItemDto(
    Guid Id,
    DateTime PaidAtUtc,
    Guid ClinicId,
    string ClinicName,
    Guid? PetId,
    string PetName,
    decimal Amount,
    string Currency,
    PaymentMethod Method,
    string? Notes);
