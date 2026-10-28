using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.OutstandingBalances;

/// <summary>Doğrulanmış ve klinik kapsamı çözülmüş süzgeç; JSON ve dışa aktarma tek yerden okur.</summary>
internal sealed record OutstandingBalancesReportFilter(
    Guid TenantId,
    Guid? ClinicId,
    IReadOnlyCollection<Guid>? AccessibleClinicIds,
    decimal? MinAmount,
    DailySearchTerm? Search)
{
    public OutstandingBalancesReadRequest ToReadRequest(int skip, int take)
        => new(TenantId, ClinicId, AccessibleClinicIds, MinAmount, Search, skip, take);

    public static async Task<Result<OutstandingBalancesReportFilter>> ResolveAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        Guid? requestClinicId,
        decimal? minAmount,
        string? search,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<OutstandingBalancesReportFilter>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (minAmount is < 0)
        {
            return Result<OutstandingBalancesReportFilter>.Failure(
                "OutstandingBalances.MinAmountInvalid",
                "minAmount negatif olamaz.");
        }

        if (ClinicContextRule.Conflicts(requestClinicId, clinicContext.ClinicId))
        {
            return Result<OutstandingBalancesReportFilter>.Failure(ClinicContextRule.Mismatch("OutstandingBalances"));
        }

        var scopeResult = await scopeResolver.ResolveAsync(tenantId, requestClinicId ?? clinicContext.ClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<OutstandingBalancesReportFilter>.Failure(scopeResult.Error);

        var scope = scopeResult.Value!;
        return Result<OutstandingBalancesReportFilter>.Success(new OutstandingBalancesReportFilter(
            tenantId,
            scope.SingleClinicId,
            scope.AccessibleClinicIds,
            minAmount,
            DailySearchTerm.Create(search)));
    }
}
