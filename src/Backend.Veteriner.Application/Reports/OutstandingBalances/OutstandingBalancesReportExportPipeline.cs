using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.OutstandingBalances.Contracts.Dtos;
using Backend.Veteriner.Application.Tenants;
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
        ITrialLimitPolicy trialLimitPolicy,
        CancellationToken ct)
    {
        var filter = await OutstandingBalancesReportFilter.ResolveAsync(
            tenantContext, clinicContext, scopeResolver, clinicId, minAmount, search, ct);
        if (!filter.IsSuccess)
            return Result<IReadOnlyList<OutstandingBalanceItemDto>>.Failure(filter.Error);

        // Satır tavanı kiracıya göre (TRIAL-004): deneme 1.000, ücretli ReportsSharedLimits.MaxExportRows.
        var rowLimit = await trialLimitPolicy.GetExportRowLimitAsync(filter.Value!.TenantId, ct);
        var read = await reader.ReadAsync(filter.Value.ToReadRequest(0, rowLimit.MaxRows), ct);

        var withinLimit = rowLimit.Ensure(read.TotalCount, "OutstandingBalances.ReportExportTooManyRows");
        if (!withinLimit.IsSuccess)
            return Result<IReadOnlyList<OutstandingBalanceItemDto>>.Failure(withinLimit.Error);

        return Result<IReadOnlyList<OutstandingBalanceItemDto>>.Success(read.Items);
    }

    /// <summary>Dosya adındaki İstanbul takvim günü.</summary>
    public static string FileDateStamp(DateTime utcNow)
        => OutstandingBalancesReportTime.ToIstanbul(utcNow).ToString("yyyyMMdd");
}
