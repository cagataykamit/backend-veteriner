using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Reports.Examinations;

internal static class ExaminationsReportQueryValidation
{
    public static async Task<Result<(Guid TenantId, Guid? EffectiveClinicId, DateTime FromUtc, DateTime ToUtc)>> ValidateAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IReadRepository<Clinic> clinics,
        Guid? requestClinicId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return Result<(Guid, Guid?, DateTime, DateTime)>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (fromUtc == default || toUtc == default)
        {
            return Result<(Guid, Guid?, DateTime, DateTime)>.Failure(
                "Examinations.ReportDateRangeInvalid",
                "Rapor için from ve to zorunludur ve geçerli UTC zamanları olmalıdır.");
        }

        if (fromUtc > toUtc)
        {
            return Result<(Guid, Guid?, DateTime, DateTime)>.Failure(
                "Examinations.ReportDateRangeInvalid",
                "from değeri to değerinden sonra olamaz.");
        }

        var spanDays = (toUtc - fromUtc).TotalDays;
        if (spanDays > ExaminationsReportConstants.MaxRangeDays)
        {
            return Result<(Guid, Guid?, DateTime, DateTime)>.Failure(
                "Examinations.ReportRangeTooLong",
                $"Tarih aralığı en fazla {ExaminationsReportConstants.MaxRangeDays} gün olabilir.");
        }

        var effectiveClinicId = requestClinicId ?? clinicContext.ClinicId;
        if (requestClinicId.HasValue && clinicContext.ClinicId.HasValue
                                      && requestClinicId.Value != clinicContext.ClinicId.Value)
        {
            return Result<(Guid, Guid?, DateTime, DateTime)>.Failure(
                "Examinations.ClinicContextMismatch",
                "Istek clinicId degeri aktif clinic baglami ile uyusmuyor.");
        }

        if (effectiveClinicId is { } ecid)
        {
            var clinic = await clinics.FirstOrDefaultAsync(new ClinicByIdSpec(tenantId, ecid), ct);
            if (clinic is null)
            {
                return Result<(Guid, Guid?, DateTime, DateTime)>.Failure(
                    "Clinics.NotFound",
                    "Klinik bulunamadı veya kiracıya ait değil.");
            }
        }

        return Result<(Guid, Guid?, DateTime, DateTime)>.Success((tenantId, effectiveClinicId, fromUtc, toUtc));
    }
}
