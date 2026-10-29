using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Charges;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>Borç raporu PDF başlığı ve sütunları (başlıklar <see cref="OutstandingBalancesReportColumns.Headers"/>).</summary>
internal static class OutstandingBalancesPdfColumns
{
    public const string Title = "Borç Raporu";

    private static readonly string[] H = OutstandingBalancesReportColumns.Headers;

    public static readonly IReadOnlyList<RegisterColumn<OutstandingBalanceItemDto>> All =
    [
        RegisterColumn<OutstandingBalanceItemDto>.Text(null, H[0], r => r.ClientName),
        RegisterColumn<OutstandingBalanceItemDto>.Text(null, H[1], r => r.Phone),
        RegisterColumn<OutstandingBalanceItemDto>.Money(null, H[2], r => r.OutstandingAmount),
        RegisterColumn<OutstandingBalanceItemDto>.Text(null, H[3], _ => Charge.DefaultCurrency),
        RegisterColumn<OutstandingBalanceItemDto>.Number(null, H[4], r => r.OpenChargeCount),
        RegisterColumn<OutstandingBalanceItemDto>.Date(null, H[5], r => r.OldestOpenChargeAtUtc),
    ];
}
