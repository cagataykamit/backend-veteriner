using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Examinations;

internal static class ExaminationsReportQueryValidation
{
    public static async Task<Result<(Guid TenantId, Guid? EffectiveClinicId, IReadOnlyCollection<Guid>? AccessibleClinicIds, DateTime FromUtc, DateTime ToUtc)>> ValidateAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        Guid? requestClinicId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (fromUtc == default || toUtc == default)
        {
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Failure(
                "Examinations.ReportDateRangeInvalid",
                "Rapor için from ve to zorunludur ve geçerli UTC zamanları olmalıdır.");
        }

        if (fromUtc > toUtc)
        {
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Failure(
                "Examinations.ReportDateRangeInvalid",
                "from değeri to değerinden sonra olamaz.");
        }

        var spanDays = (toUtc - fromUtc).TotalDays;
        if (spanDays > ExaminationsReportConstants.MaxRangeDays)
        {
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Failure(
                "Examinations.ReportRangeTooLong",
                $"Tarih aralığı en fazla {ExaminationsReportConstants.MaxRangeDays} gün olabilir.");
        }

        if (requestClinicId.HasValue && clinicContext.ClinicId.HasValue
                                      && requestClinicId.Value != clinicContext.ClinicId.Value)
        {
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Failure(
                "Examinations.ClinicContextMismatch",
                "İstek clinicId değeri aktif clinic bağlamı ile uyuşmuyor.");
        }

        var requestedClinicId = requestClinicId ?? clinicContext.ClinicId;
        var scopeResult = await scopeResolver.ResolveAsync(tenantId, requestedClinicId, ct);
        if (!scopeResult.IsSuccess)
            return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Failure(scopeResult.Error);

        var scope = scopeResult.Value!;
        return Result<(Guid, Guid?, IReadOnlyCollection<Guid>?, DateTime, DateTime)>.Success(
            (tenantId, scope.SingleClinicId, scope.AccessibleClinicIds, fromUtc, toUtc));
    }
}
