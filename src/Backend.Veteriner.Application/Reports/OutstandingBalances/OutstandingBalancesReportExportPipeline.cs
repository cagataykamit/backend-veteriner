using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>CSV ve XLSX export için ortak doğrulama + satır yükleme (JSON ile aynı okuyucu ve sıralama).</summary>
internal static class OutstandingBalancesReportExportPipeline
{
    public static async Task<Result<IReadOnlyList<OutstandingBalanceItemDto>>> LoadAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        IOutstandingBalancesReportReader reader,
        Guid? clinicId,
        decimal? minAmount,
        string? search,
        CancellationToken ct)
    {
        var filter = await OutstandingBalancesReportFilter.ResolveAsync(
            tenantContext, clinicContext, scopeResolver, clinicId, minAmount, search, ct);
        if (!filter.IsSuccess)
            return Result<IReadOnlyList<OutstandingBalanceItemDto>>.Failure(filter.Error);

        var read = await reader.ReadAsync(filter.Value!.ToReadRequest(0, OutstandingBalancesReportConstants.MaxExportRows), ct);

        if (read.TotalCount > OutstandingBalancesReportConstants.MaxExportRows)
        {
            return Result<IReadOnlyList<OutstandingBalanceItemDto>>.Failure(
                "OutstandingBalances.ReportExportTooManyRows",
                $"Dışa aktarma en fazla {OutstandingBalancesReportConstants.MaxExportRows} satır destekler; filtreyi daraltın.");
        }

        return Result<IReadOnlyList<OutstandingBalanceItemDto>>.Success(read.Items);
    }

    /// <summary>Dosya adındaki İstanbul takvim günü.</summary>
    public static string FileDateStamp(DateTime utcNow)
        => OutstandingBalancesReportTime.ToIstanbul(utcNow).ToString("yyyyMMdd");
}
