using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>
/// Müşteri bazlı kalan tutar okuyucusu (Command DB). Kural <c>Charge</c> sayaçları (<c>Amount − AllocatedAmount</c>, <see cref="Domain.Charges.Charge.OutstandingAmount"/> ile aynı);
/// JSON rapor ve dışa aktarma aynı çağrıyı kullanır.
/// </summary>
public interface IOutstandingBalancesReportReader
{
    Task<OutstandingBalancesReadResult> ReadAsync(OutstandingBalancesReadRequest request, CancellationToken ct = default);
}

/// <param name="ClinicId">Tek klinik kapsamı; null ise <paramref name="AccessibleClinicIds"/> (null = kiracı geneli).</param>
/// <param name="MinAmount">Kalan tutar alt sınırı (dahil); null = yalnız kalan &gt; 0.</param>
public sealed record OutstandingBalancesReadRequest(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    decimal? MinAmount,
    DailySearchTerm? Search,
    int Skip,
    int Take);

public sealed record OutstandingBalancesReadResult(
    int TotalCount,
    decimal TotalOutstandingAmount,
    IReadOnlyList<OutstandingBalanceItemDto> Items);
