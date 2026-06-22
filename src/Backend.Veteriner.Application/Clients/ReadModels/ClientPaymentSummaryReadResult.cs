using Backend.Veteriner.Application.Clients.Contracts.Dtos;

namespace Backend.Veteriner.Application.Clients.ReadModels;

/// <summary>
/// Query DB client payment summary okuma sonucu: SQL tarafında hesaplanan aggregate'ler + recent satırlar.
/// <para>
/// Header alanları (<c>ClientId</c>, <c>ClientName</c>) ve tek-currency <c>TotalPaidAmount</c> türetimi handler'da
/// yapılır; bu sonuç yalnız PaymentReadModels'ten okunabilen alanları taşır.
/// </para>
/// </summary>
public sealed record ClientPaymentSummaryReadResult(
    int TotalPaymentsCount,
    IReadOnlyList<ClientPaymentCurrencyTotalDto> CurrencyTotals,
    DateTime? LastPaymentAtUtc,
    IReadOnlyList<ClientPaymentRecentItemDto> RecentPayments);
