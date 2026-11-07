using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Reports.Registers;
using Backend.Veteriner.Domain.Payments;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.DailyTransactions;

/// <summary>
/// Gün sonu raporlarının ortak süzgeç çözümü: tarih aralığı + kiracı + klinik kapsamı defterlerle aynı bileşenden
/// (<see cref="RegisterReportFilter"/>) geçer; kural tek yerde kalır.
/// </summary>
internal static class DailyTransactionsReportFilter
{
    public static async Task<Result<FinanceEventFilter>> ResolveAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        DateOnly from,
        DateOnly to,
        Guid? clinicId,
        DailyTransactionKind? kind,
        PaymentMethod? paymentMethod,
        Guid? performedByUserId,
        CancellationToken ct)
    {
        var resolved = await RegisterReportFilter.ResolveAsync(
            tenantContext, clinicContext, scopeResolver, new RegisterQuery(from, to, clinicId, IncludeVoided: true), ct);
        if (!resolved.IsSuccess)
            return Result<FinanceEventFilter>.Failure(resolved.Error);

        var f = resolved.Value!;
        return Result<FinanceEventFilter>.Success(new FinanceEventFilter(
            f.TenantId, f.ClinicId, f.AccessibleClinicIds, f.FromUtc, f.ToUtcExclusive, kind, paymentMethod, performedByUserId));
    }

    /// <summary>
    /// Klinik kapsamı çağıranca çözülmüş süzgeç (patron ekranında şube başına). Aralık önceden <see cref="RegisterReportFilter.RangeError"/>
    /// ile doğrulanmış olmalı; gün sınırları <see cref="ResolveAsync"/> ile aynıdır.
    /// </summary>
    public static FinanceEventFilter ForScope(Guid tenantId, Guid? clinicId, IReadOnlyCollection<Guid>? accessibleClinicIds, DateOnly from, DateOnly to)
        => new(
            tenantId, clinicId, accessibleClinicIds,
            OperationDayBounds.ForLocalDate(from).DayStartUtc, OperationDayBounds.ForLocalDate(to).DayEndUtc,
            null, null, null);
}
