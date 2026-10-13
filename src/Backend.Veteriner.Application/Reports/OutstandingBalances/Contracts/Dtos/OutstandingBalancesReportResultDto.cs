namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;

/// <param name="TotalCount">Süzgeçlere uyan borçlu müşteri sayısı (tüm sayfalar).</param>
/// <param name="TotalOutstandingAmount">Süzgeçlere uyan tüm müşterilerin kalan toplamı (yalnız sayfa değil).</param>
public sealed record OutstandingBalancesReportResultDto(
    string Currency,
    int TotalCount,
    decimal TotalOutstandingAmount,
    IReadOnlyList<OutstandingBalanceItemDto> Items);
