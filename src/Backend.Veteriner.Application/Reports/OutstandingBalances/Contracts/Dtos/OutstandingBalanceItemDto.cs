namespace Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;

/// <summary>Bir müşterinin kapsamdaki açık ücretlerinin toplamı; <c>OldestOpenChargeAtUtc</c> yaşlandırma için ham veridir.</summary>
public sealed record OutstandingBalanceItemDto(
    Guid ClientId,
    string ClientName,
    string? Phone,
    decimal OutstandingAmount,
    int OpenChargeCount,
    DateTime OldestOpenChargeAtUtc);
